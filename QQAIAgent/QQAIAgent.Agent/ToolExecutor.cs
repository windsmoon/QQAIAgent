using System.Diagnostics;
using Microsoft.Extensions.Logging;
using QQAIAgent.Abstractions.Tools;
using QQAIAgent.Agent.Chat;

namespace QQAIAgent.Agent;

public sealed class ToolExecutor(IEnumerable<ITool> tools, IReadOnlyList<ChatToolDefinition> toolDefinitions, ILogger<ToolExecutor> logger)
{
    #region methods
    /// <summary>按顺序执行已启用的工具调用，返回带调用 ID 的工具消息；调用失败以结果文本返回，请求取消继续抛出。</summary>
    public async Task<IReadOnlyList<ChatMessage>> ExecuteAsync(IReadOnlyList<ChatToolCall> toolCalls, CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>(toolCalls.Count);
        foreach (var toolCall in toolCalls)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var toolScope = logger.BeginScope("工具={ToolName}，调用ID={ToolCallId}", toolCall.Name, toolCall.Id);

            string content;
            var toolDefinition = toolDefinitions.FirstOrDefault(definition => definition.Enabled && definition.Name == toolCall.Name);
            if (toolDefinition is null)
            {
                content = $"工具未启用或未配置：{toolCall.Name}";
                logger.LogWarning("工具调用已跳过：{Reason}", content);
            }
            else
            {
                // TODO 用 Dictionary
                var tool = tools.FirstOrDefault(candidate => candidate.Name == toolCall.Name);
                if (tool is null)
                {
                    content = $"未加载工具实现：{toolCall.Name}";
                    logger.LogWarning("工具调用已跳过：{Reason}", content);
                }
                else
                {
                    logger.LogInformation("开始执行工具。");
                    var startedAt = Stopwatch.GetTimestamp();
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(toolDefinition.TimeoutSeconds));
                    try
                    {
                        // 工具未响应取消时也结束等待；工具自身仍需配合取消，才能停止实际工作。
                        content = await tool.ExecuteAsync(toolCall.ArgumentsJson, timeout.Token).WaitAsync(timeout.Token);
                        logger.LogInformation("工具执行完成：结果字符数={ContentLength}，耗时={ElapsedMilliseconds:F0} ms",
                            content?.Length ?? 0, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        // 整个行为被主动取消，不继续执行了
                        logger.LogInformation("工具执行已取消：耗时={ElapsedMilliseconds:F0} ms", Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                        throw;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == false && timeout.IsCancellationRequested)
                    {
                        logger.LogWarning("工具执行超时：上限={TimeoutSeconds} s，耗时={ElapsedMilliseconds:F0} ms", toolDefinition.TimeoutSeconds, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                        content = $"工具执行超时：{toolCall.Name}，已超过 {toolDefinition.TimeoutSeconds} 秒";
                    }
                    catch (Exception exception) when (cancellationToken.IsCancellationRequested == false)
                    {
                        logger.LogWarning(exception, "工具执行失败：耗时={ElapsedMilliseconds:F0} ms", Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
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
