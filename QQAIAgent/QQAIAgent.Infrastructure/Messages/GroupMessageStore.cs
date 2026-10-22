using System.Globalization;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using QQAIAgent.Infrastructure.Database;
using QQAIAgent.Infrastructure.Image;
using QQAIAgent.Infrastructure.Tokenization;
using QQAIAgent.Messaging;

namespace QQAIAgent.Infrastructure.Messages;

/// <summary>只记录初始化时允许的群，每个群使用独立的 SQLite 文件。</summary>
public sealed partial class GroupMessageStore(string directoryPath, TextToImageStore textToImageStore, Tokenizer tokenizer)
{
    #region fields
    private readonly Dictionary<long, IDatabase> _databases = [];
    private readonly Dictionary<long, GroupMessageFtsStore> _ftsStores = [];
    #endregion

    #region methods
    public async Task InitializeAsync(IEnumerable<long> allowedGroups, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directoryPath);
        foreach (var groupId in allowedGroups)
        {
            var databasePath = Path.Combine(directoryPath, $"{groupId.ToString(CultureInfo.InvariantCulture)}.sqlite3");
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath
            }.ToString();
            var database = new SqliteDatabase(connectionString);
            await database.ExecuteAsync("""
                BEGIN;

                CREATE TABLE IF NOT EXISTS messages (
                    id INTEGER PRIMARY KEY,
                    message_id INTEGER,
                    message_time INTEGER NOT NULL,
                    sender_id INTEGER NOT NULL,
                    sender_nickname TEXT NOT NULL DEFAULT '',
                    sender_card TEXT NOT NULL DEFAULT '',
                    sub_type TEXT NOT NULL DEFAULT '',
                    content_json TEXT NOT NULL,
                    ref_message_id INTEGER
                );

                CREATE INDEX IF NOT EXISTS ix_messages_message_id
                    ON messages (message_id);

                CREATE INDEX IF NOT EXISTS ix_messages_time
                    ON messages (message_time);

                COMMIT;
                """, cancellationToken: cancellationToken);
            var ftsStore = new GroupMessageFtsStore(databasePath, tokenizer);
            await ftsStore.InitializeAsync(cancellationToken);
            _databases[groupId] = database;
            _ftsStores[groupId] = ftsStore;
        }
    }

    /// <summary>在同一事务内保存原消息和分词索引，不依赖业务处理时修改的消息段。</summary>
    public async Task RecordAsync(GroupMessage message, CancellationToken cancellationToken)
    {
        if (_databases.TryGetValue(message.GroupId, out var database) == false)
        {
            return;
        }

        // 使用原始正文，避免消息段已被业务处理修改；提取规则与历史索引保持一致。
        using var document = JsonDocument.Parse(message.ContentJson);
        var text = document.RootElement.ValueKind switch
        {
            JsonValueKind.String => document.RootElement.GetString() ?? "",
            JsonValueKind.Array => string.Concat(document.RootElement.EnumerateArray()
                .Where(segment => segment.GetProperty("type").GetString() == "text")
                .Select(segment => segment.GetProperty("data").GetProperty("text").GetString())),
            _ => ""
        };

        await using var connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var recordId = await connection.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO messages (message_id, message_time, sender_id, sender_nickname,
                sender_card, sub_type, content_json, ref_message_id)
            VALUES (@MessageId, @MessageTime, @SenderId, @SenderNickname,
                @SenderCard, @SubType, @ContentJson, @RefMessageId)
            RETURNING id;
            """, new
        {
            message.MessageId,
            message.MessageTime,
            message.SenderId,
            message.SenderNickname,
            message.SenderCard,
            message.SubType,
            message.ContentJson,
            message.RefMessageId
        }, transaction, cancellationToken: cancellationToken));

        await _ftsStores[message.GroupId].RecordAsync(recordId, text, connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>按群号和消息 ID 读取并还原消息，未找到时返回 null。</summary>
    public async Task<GroupMessage> GetMessageAsync(long groupId, long messageId, CancellationToken cancellationToken)
    {
        var database = _databases[groupId];
        var messages = await database.QueryAsync<GroupMessage>("""
            SELECT @GroupId AS GroupId, message_id AS MessageId, message_time AS MessageTime,
                sender_id AS SenderId, sender_nickname AS SenderNickname,
                sender_card AS SenderCard, sub_type AS SubType,
                content_json AS ContentJson, ref_message_id AS RefMessageId
            FROM messages
            WHERE message_id = @MessageId
            ORDER BY id DESC
            LIMIT 1;
            """, new { GroupId = groupId, MessageId = messageId }, cancellationToken);
        var message = messages.FirstOrDefault();
        if (message is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(message.ContentJson);
        GroupMessageParser.ParseMessageContents(message, document.RootElement);
        await textToImageStore.RestoreAsync(message, cancellationToken);
        return message;
    }

    /// <summary>读取并还原截至当前消息发送时刻的最近群消息，排除当前消息；数量限制在引用和正文类型过滤之前应用。</summary>
    public async Task<List<GroupMessage>> GetRecentMessagesAsync(long groupId, long? currentMessageId, long? currentMessageTime, int count, CancellationToken cancellationToken)
    {
        var database = _databases[groupId];
        var messageList = await database.QueryAsync<GroupMessage>("""
            SELECT @GroupId AS GroupId, message_id AS MessageId, message_time AS MessageTime,
                sender_id AS SenderId, sender_nickname AS SenderNickname,
                sender_card AS SenderCard, sub_type AS SubType,
                content_json AS ContentJson, ref_message_id AS RefMessageId
            FROM messages
            WHERE (@CurrentMessageTime IS NULL OR message_time <= @CurrentMessageTime)
                AND (@CurrentMessageId IS NULL OR message_id IS NULL OR message_id != @CurrentMessageId)
            ORDER BY message_time DESC, id DESC
            LIMIT @Count;
            """, new
        {
            GroupId = groupId,
            CurrentMessageId = currentMessageId,
            CurrentMessageTime = currentMessageTime,
            Count = count
        }, cancellationToken);

        foreach (var message in messageList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var document = JsonDocument.Parse(message.ContentJson);
            GroupMessageParser.ParseMessageContents(message, document.RootElement);
            await textToImageStore.RestoreAsync(message, cancellationToken);
        }

        return messageList;
    }
    #endregion
}
