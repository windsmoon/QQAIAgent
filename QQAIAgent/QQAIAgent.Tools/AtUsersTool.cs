using System.Text.Json;
using QQAIAgent.Abstractions.Tool;

namespace QQAIAgent.Tools;

public sealed class AtUsersTool : ITool
{
    #region properties
    public string Name => "at_users";
    #endregion

    #region methods
    public Task<ToolResult> ExecuteAsync(string argumentsJson, ToolContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var arguments = JsonDocument.Parse(argumentsJson);
        var userIds = new List<long>();
        foreach (var element in arguments.RootElement.GetProperty("user_ids").EnumerateArray())
        {
            var userId = element.GetInt64();
            if (userId <= 0)
            {
                throw new ArgumentException("user_ids 中的 QQ 号必须为正整数。", nameof(argumentsJson));
            }

            userIds.Add(userId);
        }

        if (userIds.Count == 0)
        {
            throw new ArgumentException("user_ids 至少需要一个 QQ 号。", nameof(argumentsJson));
        }

        context.AtUserIds.UnionWith(userIds);
        return Task.FromResult(new ToolResult(JsonSerializer.Serialize(new
        {
            userIds = context.AtUserIds,
            message = "已加入本次最终回复的艾特集合，将随最终回复正文一起发送。"
        })));
    }
    #endregion
}
