#nullable disable

using System.Text.Json;
using QQAIAgent.Abstractions.Messages;
using QQAIAgent.Abstractions.Tool;

namespace QQAIAgent.Tools;

public sealed class SearchGroupMessagesTool(IGroupMessageQuery messageQuery) : ITool
{
    #region fields
    private static readonly JsonSerializerOptions ResultOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    #endregion

    #region properties
    public string Name => "search_group_messages";
    #endregion

    #region methods
    public async Task<string> ExecuteAsync(string argumentsJson, ToolContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.GroupId <= 0)
        {
            throw new InvalidOperationException("缺少查询群号，不能查询群聊数据库。");
        }

        using var arguments = JsonDocument.Parse(argumentsJson);
        var root = arguments.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("群聊查询参数必须是 JSON 对象。", nameof(argumentsJson));
        }

        var propertyNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name is not ("operation" or "keyword" or "sender_id" or "sender_name" or "start_time" or "end_time"
                or "message_id" or "ref_message_id" or "limit" or "offset" or "order"))
            {
                throw new ArgumentException($"不支持的群聊查询参数：{property.Name}。只能使用预定义参数，不接受 SQL 或群号。");
            }

            if (propertyNames.Add(property.Name) == false)
            {
                throw new ArgumentException($"群聊查询参数重复：{property.Name}。");
            }
        }

        var operation = ReadString(root, "operation") ?? "search";
        if (operation is not ("search" or "count"))
        {
            throw new ArgumentException("operation 只能是 search 或 count。");
        }

        var filter = new GroupMessageFilter
        {
            Keyword = ReadString(root, "keyword"),
            SenderId = ReadInteger(root, "sender_id"),
            SenderName = ReadString(root, "sender_name"),
            StartTime = ReadInteger(root, "start_time"),
            EndTime = ReadInteger(root, "end_time"),
            MessageId = ReadInteger(root, "message_id"),
            RefMessageId = ReadInteger(root, "ref_message_id")
        };
        filter.Validate();

        if (operation == "count")
        {
            if (propertyNames.Overlaps(["limit", "offset", "order"]))
            {
                throw new ArgumentException("count 不接受 limit、offset 或 order；统计结果不分页。");
            }

            var total = await messageQuery.CountAsync(context.GroupId, filter, cancellationToken);
            return JsonSerializer.Serialize(new { groupId = context.GroupId, operation, total }, ResultOptions);
        }

        var limit = ReadInteger(root, "limit") ?? 20;
        var offset = ReadInteger(root, "offset") ?? 0;
        if (limit is < 1 or > 50 || offset is < 0 or > 10000)
        {
            throw new ArgumentException("limit 必须为 1 到 50，offset 必须为 0 到 10000。");
        }

        var order = ReadString(root, "order") ?? "desc";
        if (order is not ("asc" or "desc"))
        {
            throw new ArgumentException("order 只能是 asc（从旧到新）或 desc（从新到旧）。");
        }

        var result = await messageQuery.SearchAsync(context.GroupId, filter, (int)limit, (int)offset, order == "asc", cancellationToken);
        return JsonSerializer.Serialize(new
        {
            groupId = context.GroupId,
            operation,
            order,
            offset,
            limit,
            returnedCount = result.Messages.Count,
            result.HasMore,
            nextOffset = result.HasMore && offset + limit <= 10000 ? (long?)(offset + limit) : null,
            result.Messages
        }, ResultOptions);
    }

    private static string ReadString(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var value)
            ? value.GetString() ?? throw new ArgumentException($"{name} 不能为 null。")
            : null;
    }

    private static long? ReadInteger(JsonElement root, string name)
    {
        return root.TryGetProperty(name, out var value) ? value.GetInt64() : null;
    }
    #endregion
}
