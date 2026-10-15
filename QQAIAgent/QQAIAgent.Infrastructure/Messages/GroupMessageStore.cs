using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using QQAIAgent.Infrastructure.Database;
using QQAIAgent.Messaging;

namespace QQAIAgent.Infrastructure.Messages;

/// <summary>只记录初始化时允许的群，每个群使用独立的 SQLite 文件。</summary>
public sealed partial class GroupMessageStore(string directoryPath)
{
    #region fields
    private readonly Dictionary<long, IDatabase> _databases = [];
    #endregion

    #region methods
    public async Task InitializeAsync(IEnumerable<long> allowedGroups, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directoryPath);
        foreach (var groupId in allowedGroups)
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(directoryPath, $"{groupId.ToString(CultureInfo.InvariantCulture)}.sqlite3")
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
            _databases[groupId] = database;
        }
    }

    /// <summary>保存发送信息和完整原始正文，不依赖业务处理时修改的消息段。</summary>
    public Task RecordAsync(GroupMessage message, CancellationToken cancellationToken)
    {
        if (_databases.TryGetValue(message.GroupId, out var database) == false)
        {
            return Task.CompletedTask;
        }

        return database.ExecuteAsync("""
            INSERT INTO messages (message_id, message_time, sender_id, sender_nickname,
                sender_card, sub_type, content_json, ref_message_id)
            VALUES (@MessageId, @MessageTime, @SenderId, @SenderNickname,
                @SenderCard, @SubType, @ContentJson, @RefMessageId);
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
        }, cancellationToken);
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
        }

        return messageList;
    }
    #endregion
}
