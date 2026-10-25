using System.Data.Common;
using System.Diagnostics;
using Dapper;
using Microsoft.Data.Sqlite;
using QQAIAgent.Abstractions.Messages;
using QQAIAgent.Infrastructure.Database;
using QQAIAgent.Infrastructure.Embedding;

namespace QQAIAgent.Infrastructure.Messages;

/// <summary>在已有群消息数据库中维护向量表，通过本地记录主键关联原消息。</summary>
public sealed class GroupMessageVectorStore
{
    #region fields
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
    #endregion

    #region constructors
    public GroupMessageVectorStore(string databasePath)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = SqliteOpenMode.ReadWrite
        }.ToString();
        _database = new SqliteDatabase(connectionString);
    }
    #endregion

    #region methods
    /// <summary>建立向量表，保留已有向量，不自动生成历史消息向量。</summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return _database.ExecuteAsync("""
            CREATE VIRTUAL TABLE IF NOT EXISTS messages_vec USING vec0(
                record_id INTEGER PRIMARY KEY,
                embedding FLOAT[384] distance_metric=cosine
            );
            """, cancellationToken: cancellationToken);
    }

    /// <summary>沿用原消息入库的连接和事务写入已生成的向量，由调用方统一提交。</summary>
    internal Task RecordAsync(long recordId, float[] vector, DbConnection connection, DbTransaction transaction, CancellationToken cancellationToken)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO messages_vec(record_id, embedding) VALUES (@RecordId, @Embedding);
            """, new { RecordId = recordId, Embedding = bytes }, transaction, cancellationToken: cancellationToken));
    }

    /// <summary>停服维护时清空并全量生成向量；跳过空文本，中断后再次调用从头重建。调用方负责复用和释放模型。</summary>
    public async Task<(long Count, long Skipped, TimeSpan Elapsed)> BuildAsync(EmbeddingGenerator generator, int batchSize = 500, CancellationToken cancellationToken = default)
    {
        batchSize = Math.Max(1, batchSize);
        await InitializeAsync(cancellationToken);
        await using var connection = _database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        await connection.ExecuteAsync(new CommandDefinition("DELETE FROM messages_vec;", cancellationToken: cancellationToken));

        long? lastId = null;
        long count = 0;
        long skipped = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 与 FTS 使用相同的原文提取规则：字符串直接使用，数组按原顺序拼接 text 段。
            var messages = (await connection.QueryAsync<GroupMessageSearchItem>(new CommandDefinition($"""
                SELECT messages.id AS RecordId, {MessageTextSql} AS Text
                FROM messages
                WHERE (@LastId IS NULL OR messages.id > @LastId)
                ORDER BY messages.id
                LIMIT @BatchSize;
                """, new { LastId = lastId, BatchSize = batchSize }, cancellationToken: cancellationToken))).AsList();
            if (messages.Count == 0)
            {
                break;
            }

            // Build 只在离线时执行，不用管 gc 了。
            // 先生成本批向量，再开启短事务写入，避免模型推理期间持有写锁。
            var vectors = new List<(long RecordId, byte[] Embedding)>();
            foreach (var message in messages)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(message.Text))
                {
                    continue;
                }
                var vector = generator.Generate(message.Text, "passage");
                // sqlite-vec 的 float 向量使用连续 float32 字节，直接保存为 BLOB。
                var bytes = new byte[vector.Length * sizeof(float)];
                Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
                vectors.Add((message.RecordId, bytes));
            }

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            foreach (var vector in vectors)
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO messages_vec(record_id, embedding) VALUES (@RecordId, @Embedding);
                    """, new { vector.RecordId, vector.Embedding }, transaction, cancellationToken: cancellationToken));
            }
            await transaction.CommitAsync(cancellationToken);
            // 游标按读取到的最后一条消息推进，整批空文本也能继续；不保存断点状态。
            lastId = messages[^1].RecordId;
            count += vectors.Count;
            skipped += messages.Count - vectors.Count;
        }

        stopwatch.Stop();
        return (count, skipped, stopwatch.Elapsed);
    }

    /// <summary>用 query 前缀检索相似聊天，按余弦距离升序返回；不自动建表或生成历史向量。</summary>
    public async Task<List<(GroupMessageSearchItem Message, double Distance)>> SearchAsync(string text, EmbeddingGenerator generator, int limit = 20, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (limit < 1)
        {
            throw new ArgumentException("limit 必须大于 0。", nameof(limit));
        }
        var vector = generator.Generate(text, "query");
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);

        await using var connection = _database.CreateConnection();
        // 先在向量表内取最近的 k 条，再用本地记录 ID 关联原文；距离越小越相近。
        var results = await connection.QueryAsync<GroupMessageSearchItem, double, (GroupMessageSearchItem Message, double Distance)>(
            new CommandDefinition($"""
                WITH nearest AS (
                    SELECT record_id, distance
                    FROM messages_vec
                    WHERE embedding MATCH @Embedding AND k = @Limit
                )
                SELECT messages.id AS RecordId, messages.message_id AS MessageId,
                    messages.message_time AS MessageTime, messages.sender_id AS SenderId,
                    messages.sender_nickname AS SenderNickname, messages.sender_card AS SenderCard,
                    messages.ref_message_id AS RefMessageId, {MessageTextSql} AS Text,
                    nearest.distance AS Distance
                FROM nearest
                JOIN messages ON messages.id = nearest.record_id
                ORDER BY nearest.distance, messages.id DESC;
                """, new { Embedding = bytes, Limit = limit }, cancellationToken: cancellationToken),
            (message, distance) => (message, distance), splitOn: "Distance");
        return results.AsList();
    }
    #endregion
}
