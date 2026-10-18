using QQAIAgent.Abstractions.Messages;
using QQAIAgent.Infrastructure.Database;

namespace QQAIAgent.Infrastructure.Messages;

public sealed partial class GroupMessageStore : IGroupMessageQuery
{
    #region fields
    private const string FilteredMessagesSql = """
        WITH filtered_messages AS (
            SELECT *,
                CASE json_type(content_json)
                    WHEN 'text' THEN json_extract(content_json, '$')
                    WHEN 'array' THEN (
                        SELECT COALESCE(group_concat(json_extract(segment.value, '$.data.text'), '' ORDER BY segment.key), '')
                        FROM json_each(messages.content_json) AS segment
                        WHERE json_extract(segment.value, '$.type') = 'text'
                    )
                    ELSE ''
                END AS message_text
            FROM messages
            WHERE (@SenderId IS NULL OR sender_id = @SenderId)
                AND (@SenderName IS NULL OR instr(lower(sender_nickname), lower(@SenderName)) > 0
                    OR instr(lower(sender_card), lower(@SenderName)) > 0)
                AND (@StartTime IS NULL OR message_time >= @StartTime)
                AND (@EndTime IS NULL OR message_time <= @EndTime)
                AND (@MessageId IS NULL OR message_id = @MessageId)
                AND (@RefMessageId IS NULL OR ref_message_id = @RefMessageId)
        )
        """;
    private const string KeywordFilterSql = """
        WHERE (@Keyword IS NULL OR instr(lower(message_text), lower(@Keyword)) > 0)
        """;
    #endregion

    #region methods
    public async Task<GroupMessageSearchResult> SearchAsync(long groupId, GroupMessageFilter filter, int limit, int offset,
        bool oldestFirst, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(filter);
        filter.Validate();
        if (limit < 1 || offset < 0 )
        {
            throw new ArgumentException("limit 必须为大于等于 1， offset 必须大于等于 0。");
        }

        var database = GetQueryDatabase(groupId);
        var orderSql = oldestFirst ? " ORDER BY message_time ASC, id ASC" : " ORDER BY message_time DESC, id DESC";
        var messages = await database.QueryAsync<GroupMessageSearchItem>(FilteredMessagesSql + """

            SELECT id AS RecordId, message_id AS MessageId, message_time AS MessageTime,
                sender_id AS SenderId, sender_nickname AS SenderNickname, sender_card AS SenderCard,
                ref_message_id AS RefMessageId, substr(message_text, 1, 2000) AS Text,
                length(message_text) > 2000 AS TextTruncated
            FROM filtered_messages
            """ + "\n" + KeywordFilterSql + orderSql + " LIMIT @Count OFFSET @Offset;", new
        {
            filter.Keyword,
            filter.SenderId,
            filter.SenderName,
            filter.StartTime,
            filter.EndTime,
            filter.MessageId,
            filter.RefMessageId,
            Count = limit + 1,
            Offset = offset
        }, cancellationToken);

        var hasMore = messages.Count > limit;
        if (hasMore)
        {
            messages.RemoveAt(messages.Count - 1);
        }

        return new GroupMessageSearchResult { Messages = messages, HasMore = hasMore };
    }

    public Task<long> CountAsync(long groupId, GroupMessageFilter filter, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(filter);
        filter.Validate();
        var database = GetQueryDatabase(groupId);
        return database.ExecuteScalarAsync<long>(FilteredMessagesSql + "\nSELECT COUNT(*) FROM filtered_messages\n" + KeywordFilterSql + ";",
            filter, cancellationToken);
    }

    /// <summary>按时间范围分页读取完整文本，用于总结；起止时间均为包含边界的 Unix 秒，按时间和记录 Id 升序返回。</summary>
    public Task<List<GroupMessageSearchItem>> GetMessagesInTimeRangeAsync(long groupId, long startTime, long endTime,
        int limit, int offset, CancellationToken cancellationToken)
    {
        var database = GetQueryDatabase(groupId);
        var filter = new GroupMessageFilter { StartTime = startTime, EndTime = endTime };
        // 搜索工具的正文预览会截断到 2000 字，总结需要读取完整正文。
        return database.QueryAsync<GroupMessageSearchItem>(FilteredMessagesSql + """

            SELECT id AS RecordId, message_id AS MessageId, message_time AS MessageTime,
                sender_id AS SenderId, sender_nickname AS SenderNickname, sender_card AS SenderCard,
                ref_message_id AS RefMessageId, message_text AS Text
            FROM filtered_messages
            ORDER BY message_time ASC, id ASC
            LIMIT @Count OFFSET @Offset;
            """, new
        {
            filter.SenderId,
            filter.SenderName,
            filter.StartTime,
            filter.EndTime,
            filter.MessageId,
            filter.RefMessageId,
            Count = limit,
            Offset = offset
        }, cancellationToken);
    }

    private IDatabase GetQueryDatabase(long groupId)
    {
        if (groupId <= 0 || _databases.TryGetValue(groupId, out var database) == false)
        {
            throw new InvalidOperationException("当前群未启用消息记录，不能查询群聊数据库。");
        }

        return database;
    }
    #endregion
}
