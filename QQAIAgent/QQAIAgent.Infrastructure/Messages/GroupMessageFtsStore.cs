using System.Globalization;
using System.Text;
using Dapper;
using Microsoft.Data.Sqlite;
using QQAIAgent.Abstractions.Messages;
using QQAIAgent.Infrastructure.Database;
using QQAIAgent.Infrastructure.Tokenization;

namespace QQAIAgent.Infrastructure.Messages;

/// <summary>为一个已有群消息数据库维护普通 FTS5 索引，分词结果直接保存在 FTS 表中。</summary>
public sealed class GroupMessageFtsStore
{
    #region fields
    // 与现有群消息搜索保持一致：字符串正文直接使用，数组只按原顺序拼接 text 段。
    private const string MessageTextSql = """
        CASE json_type(messages.content_json)
            WHEN 'text' THEN json_extract(messages.content_json, '$')
            WHEN 'array' THEN (
                SELECT COALESCE(group_concat(json_extract(segment.value, '$.data.text'), '' ORDER BY segment.key), '')
                FROM json_each(messages.content_json) AS segment
                WHERE json_extract(segment.value, '$.type') = 'text'
            )
            ELSE ''
        END
        """;
    private readonly IDatabase _database;
    private readonly Tokenizer _tokenizer;
    #endregion

    #region constructors
    /// <summary>复用已初始化的分词器；数据库必须已经存在，不创建新的数据库文件。</summary>
    public GroupMessageFtsStore(string databasePath, Tokenizer tokenizer)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadWrite
        }.ToString();
        _database = new SqliteDatabase(connectionString);
        _tokenizer = tokenizer;
    }
    #endregion

    #region methods
    /// <summary>建立 FTS 表及旧索引清理触发器，不修改原消息结构，也不自动对历史消息分词。</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""
            CREATE VIRTUAL TABLE IF NOT EXISTS messages_fts USING fts5(
                tokens,
                tokenize='unicode61'
            );

            CREATE TRIGGER IF NOT EXISTS messages_fts_after_delete
            AFTER DELETE ON messages BEGIN
                DELETE FROM messages_fts WHERE rowid = old.id;
            END;

            CREATE TRIGGER IF NOT EXISTS messages_fts_after_update
            AFTER UPDATE OF id, content_json ON messages
            WHEN old.id != new.id OR old.content_json IS NOT new.content_json
            BEGIN
                DELETE FROM messages_fts WHERE rowid = old.id;
            END;
            """, transaction: transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// 分批处理已有消息，默认只补建缺失索引；返回本次处理的消息条数。
    /// rebuild 会先清空 FTS 再重新分词，用于词典变更；中断后可用普通补建继续，重建期间搜索结果不完整。
    /// 只处理开始时最大 id 以内的消息；已提交批次的后续正文修改会清除对应索引，留待下一次补建。
    /// </summary>
    public async Task<long> BuildAsync(bool rebuild = false, int batchSize = 500, CancellationToken cancellationToken = default)
    {
        if (batchSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "每批消息数量必须大于 0。");
        }

        await InitializeAsync(cancellationToken);
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        if (rebuild)
        {
            await connection.ExecuteAsync(new CommandDefinition("DELETE FROM messages_fts;", cancellationToken: cancellationToken));
        }

        var maxId = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
            "SELECT MAX(id) FROM messages;", cancellationToken: cancellationToken));
        long? lastId = null;
        long processedCount = 0;
        while (maxId.HasValue)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 同一批的读取、分词和写入处于同一写事务，避免分词期间正文被其他连接改写或删除。
            // 事务只覆盖一批；分词器已由调用方初始化，多群数据库可顺序复用同一个实例。
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var messages = (await connection.QueryAsync<GroupMessageSearchItem>(new CommandDefinition($"""
                SELECT messages.id AS RecordId, {MessageTextSql} AS Text
                FROM messages
                WHERE (@LastId IS NULL OR messages.id > @LastId)
                    AND messages.id <= @MaxId
                    AND NOT EXISTS (SELECT 1 FROM messages_fts WHERE rowid = messages.id)
                ORDER BY messages.id
                LIMIT @BatchSize;
                """, new { LastId = lastId, MaxId = maxId, BatchSize = batchSize },
                transaction, cancellationToken: cancellationToken))).AsList();
            if (messages.Count == 0)
            {
                await transaction.CommitAsync(cancellationToken);
                break;
            }

            foreach (var message in messages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // 索引固定使用搜索模式；空正文也保存一行空词元，表示已处理，避免每次重复扫描。
                var tokens = string.Join(" ", _tokenizer.Tokenize(message.Text, "search"));
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT OR REPLACE INTO messages_fts(rowid, tokens) VALUES (@Id, @Tokens);
                    """, new { Id = message.RecordId, Tokens = tokens }, transaction, cancellationToken: cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);
            lastId = messages[^1].RecordId;
            processedCount += messages.Count;
        }

        return processedCount;
    }

    /// <summary>将查询分词后按 AND 匹配，按相关性及消息 id 倒序返回原消息文本；不接受用户输入的 FTS 运算符。</summary>
    public async Task<List<GroupMessageSearchItem>> SearchAsync(string text, int limit = 20, int offset = 0,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (limit < 1 || offset < 0)
        {
            throw new ArgumentException("limit 必须大于 0，offset 必须大于等于 0。");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        // unicode61 默认索引字母、数字和私用字符；纯标点不能作为必须命中的查询词。
        var words = _tokenizer.Tokenize(text, "search")
            .Where(word => word.EnumerateRunes().Any(rune => Rune.IsLetter(rune) || Rune.IsNumber(rune)
                || Rune.GetUnicodeCategory(rune) == UnicodeCategory.PrivateUse))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (words.Count == 0)
        {
            return [];
        }

        // SQL 参数化之外，还需按 FTS 字符串语法转义双引号，防止输入被解释为列名或运算符。
        var query = string.Join(" AND ", words.Select(word => "\"" + word.Replace("\"", "\"\"") + "\""));
        return await _database.QueryAsync<GroupMessageSearchItem>($"""
            SELECT messages.id AS RecordId, messages.message_id AS MessageId,
                messages.message_time AS MessageTime, messages.sender_id AS SenderId,
                messages.sender_nickname AS SenderNickname, messages.sender_card AS SenderCard,
                messages.ref_message_id AS RefMessageId, {MessageTextSql} AS Text
            FROM messages_fts
            JOIN messages ON messages.id = messages_fts.rowid
            WHERE messages_fts MATCH @Query
            ORDER BY messages_fts.rank, messages.id DESC
            LIMIT @Limit OFFSET @Offset;
            """, new { Query = query, Limit = limit, Offset = offset }, cancellationToken);
    }
    #endregion
}
