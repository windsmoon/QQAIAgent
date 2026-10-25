using System.Data.Common;
using System.Diagnostics;
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
    private const string CreateTableSql = """
        CREATE VIRTUAL TABLE IF NOT EXISTS messages_fts USING fts5(
            tokens,
            tokenize='unicode61'
        );
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
    /// <summary>建立 FTS 表，不修改原消息结构，也不自动对历史消息分词。</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(CreateTableSql,
            transaction: transaction, cancellationToken: cancellationToken));
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>对传入的原始文本分词并写入 FTS；沿用消息入库的连接和事务，由调用方统一提交。</summary>
    internal async Task RecordAsync(long recordId, string text, DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken)
    {
        var tokens = string.Join(" ", _tokenizer.Tokenize(text, "search"));

        // 使用本地记录主键关联索引，QQ 消息 ID 可能重复或为空。
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO messages_fts(rowid, tokens) VALUES (@RecordId, @Tokens);
            """, new { RecordId = recordId, Tokens = tokens }, transaction, cancellationToken: cancellationToken));
    }

    /// <summary>
    /// 每次先清空 FTS，再从头分批读取全部消息、重新分词并建立索引；返回处理条数和不含初始化的处理耗时。
    /// 重建期间搜索结果不完整，中断后再次调用也会从头重建。
    /// </summary>
    public async Task<(long Count, TimeSpan Elapsed)> BuildAsync(int batchSize = 500, CancellationToken cancellationToken = default)
    {
        batchSize = Math.Max(1, batchSize);

        await InitializeAsync(cancellationToken);
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        // 调用方先加载并预热分词器；这里只统计清空索引及逐批读写、分词的时间。
        var stopwatch = Stopwatch.StartNew();
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM messages_fts;", cancellationToken: cancellationToken));

        long? lastId = null;
        long processedCount = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 同一批的读取、分词和写入处于同一事务，失败或取消时整批回滚。
            // 事务只覆盖一批；分词器已由调用方初始化，多群数据库可顺序复用同一个实例。
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var messages = (await connection.QueryAsync<GroupMessageSearchItem>(new CommandDefinition($"""
                SELECT messages.id AS RecordId, {MessageTextSql} AS Text
                FROM messages
                WHERE (@LastId IS NULL OR messages.id > @LastId)
                ORDER BY messages.id
                LIMIT @BatchSize;
                """, new { LastId = lastId, BatchSize = batchSize },
                transaction, cancellationToken: cancellationToken))).AsList();
            if (messages.Count == 0)
            {
                await transaction.CommitAsync(cancellationToken);
                break;
            }

            foreach (var message in messages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // 索引固定使用搜索模式，FTS 行与原消息通过 id 一一对应。
                var tokens = string.Join(" ", _tokenizer.Tokenize(message.Text, "search"));
                // 重建期间新消息可能已实时写入 FTS，覆盖同一行以避免主键冲突。
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT OR REPLACE INTO messages_fts(rowid, tokens) VALUES (@Id, @Tokens);
                    """, new { Id = message.RecordId, Tokens = tokens }, transaction, cancellationToken: cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);
            lastId = messages[^1].RecordId;
            processedCount += messages.Count;
        }

        stopwatch.Stop();
        return (processedCount, stopwatch.Elapsed);
    }

    /// <summary>将关键词分词后按指定的 and/or 组合词元，按相关性及消息 id 倒序返回原消息文本；关键词作为字面文本处理。</summary>
    public async Task<List<GroupMessageSearchItem>> SearchAsync(List<string> keywords, string ftsOperator, int limit = 20, int offset = 0, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (limit < 1 || offset < 0)
        {
            throw new ArgumentException("limit 必须大于 0，offset 必须大于等于 0。");
        }

        var separator = ftsOperator switch
        {
            "and" => " AND ",
            "or" => " OR ",
            _ => throw new ArgumentException("ftsOperator 只能是 and 或 or。", nameof(ftsOperator))
        };

        if (keywords.Count == 0)
        {
            return null;
        }

        // unicode61 默认索引字母、数字和私用字符；纯标点不能作为必须命中的查询词。
        var words = keywords
            .Where(keyword => string.IsNullOrWhiteSpace(keyword) == false)
            .SelectMany(keyword => _tokenizer.Tokenize(keyword, "search"))
            .Where(word => word.EnumerateRunes().Any(rune => Rune.IsLetter(rune) || Rune.IsNumber(rune)
                || Rune.GetUnicodeCategory(rune) == UnicodeCategory.PrivateUse))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (words.Count == 0)
        {
            return null;
        }

        // SQL 参数化之外，还需按 FTS 字符串语法转义双引号，防止输入被解释为列名或运算符。
        var query = string.Join(separator, words.Select(word => "\"" + word.Replace("\"", "\"\"") + "\""));
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
