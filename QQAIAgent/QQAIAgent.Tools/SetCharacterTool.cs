using System.Text;
using System.Text.Json;
using QQAIAgent.Abstractions.Character;
using QQAIAgent.Abstractions.Tool;
using QQAIAgent.Messaging;

namespace QQAIAgent.Tools;

public sealed class SetCharacterTool(ICharacterStore characterStore, IMessageReader messageReader) : ITool
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
        var operation = root.GetProperty("operation").GetString();
        if (operation == "get")
        {
            var character = await characterStore.GetAsync(context.SenderId, context.GroupId, cancellationToken);
            return new ToolResult($"已保存的助手回复人设：\n回复当前提问者的个人人设（跨群生效）：\n{character.PersonalContent ?? "未设置"}\n\n当前群的共享人设：\n{character.GroupContent ?? "未设置"}");
        }

        long? groupId = root.GetProperty("scope").GetString() switch
        {
            "personal" => null,
            "group" => context.GroupId,
            _ => throw new ArgumentException("scope 必须为 personal 或 group。", nameof(argumentsJson))
        };
        if (groupId.HasValue)
        {
            // 群人设由全群共享，设置和删除均通过群成员接口核验实际权限。
            var role = await messageReader.GetGroupMemberRoleAsync(context.GroupId, context.SenderId, cancellationToken);
            if (role != "owner" && role != "admin")
            {
                return new ToolResult("只有当前群的群主或管理员可以设置或删除群人设，未修改人设。");
            }
        }

        var scopeName = groupId.HasValue ? "当前群的共享人设" : "助手回复你时采用的个人人设（跨群生效）";
        switch (operation)
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
                return new ToolResult($"已设置{scopeName}，将在下次对话创建时按个人人设、群人设、全局默认人设的优先级加载。");
            case "delete":
                characterStore.Delete(context.SenderId, groupId);
                return new ToolResult($"已删除{scopeName}，将在下次对话创建时按个人人设、群人设、全局默认人设的优先级加载。");
            default:
                throw new ArgumentException("operation 必须为 set、delete 或 get。", nameof(argumentsJson));
        }
    }
    #endregion
}
