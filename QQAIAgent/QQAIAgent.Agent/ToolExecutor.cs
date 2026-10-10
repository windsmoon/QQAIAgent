using QQAIAgent.Abstractions.Tools;
using QQAIAgent.Agent.Chat;

namespace QQAIAgent.Agent;

public sealed class ToolExecutor(IEnumerable<ITool> tools, IReadOnlyList<ChatToolDefinition> toolDefinitions)
{
    #region methods
    /// <summary>按顺序执行已启用的工具调用，返回带调用 ID 的工具消息；调用失败以结果文本返回，请求取消继续抛出。</summary>
    public async Task<IReadOnlyList<ChatMessage>> ExecuteAsync(IReadOnlyList<ChatToolCall> toolCalls, CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>(toolCalls.Count);
        foreach (var toolCall in toolCalls)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string content;
            if (toolDefinitions.Any(definition => definition.Enabled && definition.Name == toolCall.Name) == false)
            {
                content = $"工具未启用或未配置：{toolCall.Name}";
            }
            else
            {
                var tool = tools.FirstOrDefault(candidate => candidate.Name == toolCall.Name);
                if (tool is null)
                {
                    content = $"未加载工具实现：{toolCall.Name}";
                }
                else
                {
                    try
                    {
                        content = await tool.ExecuteAsync(toolCall.ArgumentsJson, cancellationToken);
                    }
                    catch (Exception exception) when (cancellationToken.IsCancellationRequested == false)
                    {
                        content = $"工具执行失败：{toolCall.Name}，{exception.Message}";
                    }
                }
            }

            messages.Add(new ChatMessage
            {
                Role = ChatRole.Tool,
                ToolCallId = toolCall.Id,
                Content = content
            });
        }

        return messages;
    }
    #endregion
}
