using System.Text;
using System.Text.Json;
using QQAIAgent.Abstractions.Character;
using QQAIAgent.Abstractions.Tool;

namespace QQAIAgent.Tools;

public sealed class SetCharacterTool(ICharacterStore characterStore) : ITool
{
    #region properties
    public string Name => "set_character";
    #endregion

    #region methods
    public async Task<ToolResult> ExecuteAsync(string argumentsJson, ToolContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var arguments = JsonDocument.Parse(argumentsJson);
        var root = arguments.RootElement;
        long? groupId = root.GetProperty("scope").GetString() switch
        {
            "global" => null,
            "group" => context.GroupId,
            _ => throw new ArgumentException("scope 必须为 global 或 group。", nameof(argumentsJson))
        };
        var scopeName = groupId.HasValue ? "当前群内" : "全局";
        switch (root.GetProperty("operation").GetString())
        {
            case "set":
                var content = root.GetProperty("content").GetString();
                if (string.IsNullOrWhiteSpace(content))
                {
                    return new ToolResult("人设内容不能为空。");
                }

                // 按 Unicode 码点计数，与工具参数的 maxLength 一致，避免将单个表情算成两个字。
                if (content.Length > 300)
                {
                    return new ToolResult("人设不得超过 300 字符，未保存，请缩短后重新设置。");
                }

                await characterStore.SaveAsync(context.SenderId, groupId, content, cancellationToken);
                return new ToolResult($"已设置你的{scopeName}人设，将在下次对话创建时按群内、全局、默认的优先级加载。");
            case "delete":
                characterStore.Delete(context.SenderId, groupId);
                return new ToolResult($"已删除你的{scopeName}人设，将在下次对话创建时按群内、全局、默认的优先级加载。");
            default:
                throw new ArgumentException("operation 必须为 set 或 delete。", nameof(argumentsJson));
        }
    }
    #endregion
}
