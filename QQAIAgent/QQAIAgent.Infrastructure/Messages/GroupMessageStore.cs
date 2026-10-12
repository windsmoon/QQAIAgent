using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using QQAIAgent.Infrastructure.Database;

namespace QQAIAgent.Infrastructure.Messages;

/// <summary>只记录初始化时允许的群，每个群使用独立的 SQLite 文件。</summary>
public sealed class GroupMessageStore(string directoryPath)
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

    /// <summary>从群消息上报中保存发送信息和完整正文，不保存完整报文，也不依赖正文类型是否受支持。</summary>
    public Task RecordAsync(JsonElement messageEvent, CancellationToken cancellationToken)
    {
        var groupId = ReadInteger(messageEvent.GetProperty("group_id"));
        if (_databases.TryGetValue(groupId, out var database) == false)
        {
            return Task.CompletedTask;
        }

        var message = messageEvent.GetProperty("message");
        messageEvent.TryGetProperty("sender", out var sender);
        return database.ExecuteAsync("""
            INSERT INTO messages (message_id, message_time, sender_id, sender_nickname,
                sender_card, sub_type, content_json, ref_message_id)
            VALUES (@MessageId, @MessageTime, @SenderId, @SenderNickname,
                @SenderCard, @SubType, @ContentJson, @RefMessageId);
            """, new
        {
            MessageId = messageEvent.TryGetProperty("message_id", out var messageId) && messageId.ValueKind != JsonValueKind.Null
                ? ReadInteger(messageId)
                : (long?)null,
            MessageTime = ReadInteger(messageEvent.GetProperty("time")),
            SenderId = ReadInteger(messageEvent.GetProperty("user_id")),
            SenderNickname = sender.ValueKind == JsonValueKind.Object && sender.TryGetProperty("nickname", out var nickname)
                ? nickname.GetString() ?? ""
                : "",
            SenderCard = sender.ValueKind == JsonValueKind.Object && sender.TryGetProperty("card", out var card)
                ? card.GetString() ?? ""
                : "",
            SubType = messageEvent.TryGetProperty("sub_type", out var subType) ? subType.GetString() ?? "" : "",
            // 只保存 message 字段原值，数组消息和 CQ 字符串都保留原有内容及顺序。
            ContentJson = message.GetRawText(),
            RefMessageId = ReadReferenceMessageId(message)
        }, cancellationToken);
    }

    private static long? ReadReferenceMessageId(JsonElement message)
    {
        long? referenceMessageId = null;
        if (message.ValueKind == JsonValueKind.Array)
        {
            foreach (var segment in message.EnumerateArray())
            {
                if (segment.GetProperty("type").GetString() == "reply")
                {
                    referenceMessageId = ReadInteger(segment.GetProperty("data").GetProperty("id"));
                }
            }
        }
        else if (message.ValueKind == JsonValueKind.String)
        {
            // 与消息解析保持一致，只识别未转义的 CQ 引用，多个引用取最后一个。
            foreach (Match match in Regex.Matches(message.GetString() ?? "", @"\[CQ:reply(?<data>(?:,[a-zA-Z0-9_]+=[^,\]]*)*)\]"))
            {
                foreach (var parameter in match.Groups["data"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (parameter.StartsWith("id=", StringComparison.Ordinal))
                    {
                        referenceMessageId = long.Parse(parameter[3..], CultureInfo.InvariantCulture);
                    }
                }
            }
        }

        return referenceMessageId;
    }

    private static long ReadInteger(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : long.Parse(value.GetString() ?? "", CultureInfo.InvariantCulture);
    }
    #endregion
}
