using System.Reflection;
using System.Text.Json;
using QQAIAgent.Abstractions.Tool;
using QQAIAgent.Infrastructure.Runtime;
using QQAIAgent.Messaging;

namespace QQAIAgent.Tools;

public sealed class SetRuntimeDataTool(RuntimeDataStore runtimeDataStore, IMessageReader messageReader) : ITool
{
    #region properties
    public string Name => "set_runtime_data";
    #endregion

    #region methods
    public async Task<ToolResult> ExecuteAsync(string argumentsJson, ToolContext context, CancellationToken cancellationToken)
    {
        // 群号和发送者来自请求上下文，权限由接口核验，不采信模型或用户自称的身份。
        if (await messageReader.GetGroupMemberRoleAsync(context.GroupId, context.SenderId, cancellationToken) != "owner")
        {
            return new ToolResult("只有当前群的群主可以设置运行时数据，未修改设置。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var arguments = JsonDocument.Parse(argumentsJson);
        var propertyName = arguments.RootElement.GetProperty("property_name").GetString() ?? "";
        var property = typeof(RuntimeData).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property?.SetMethod?.IsPublic != true || property.GetIndexParameters().Length > 0)
        {
            return new ToolResult($"未找到可写的运行时属性：{propertyName}，未修改设置。");
        }

        var value = arguments.RootElement.GetProperty("value").Deserialize(property.PropertyType);
        property.SetValue(runtimeDataStore.Get(context.GroupId), value);
        runtimeDataStore.Save(context.GroupId);
        return new ToolResult($"已设置当前群的 {property.Name} 并保存。");
    }
    #endregion
}
