using System.Diagnostics;
using Microsoft.Extensions.Logging;
using QQAIAgent.Abstractions.Tool;
using QQAIAgent.Agent.Chat;
using QQAIAgent.Agent.Chat.Content;
using QQAIAgent.Agent.Statistics;

namespace QQAIAgent.Agent;

public sealed class ToolExecutor(IReadOnlyDictionary<string, ITool> tools, IReadOnlyDictionary<string, ChatToolDefinition> toolDefinitions, RequestStatistics statistics, ILogger<ToolExecutor> logger)
{
    #region methods
    /// <summary>按顺序执行工具调用；Agent 的前置调用不受主模型启用状态限制，调用失败以结果文本返回，请求取消继续抛出。</summary>
    public async Task<IReadOnlyList<ChatMessage>> ExecuteAsync(IReadOnlyList<ChatToolCall> toolCalls, ToolContext context, string requestId, string modelCallId, CancellationToken cancellationToken, bool isPreprocessing = false)
    {
        var messages = new List<ChatMessage>(toolCalls.Count);
        // 首轮前置调用占用下标 0，主模型返回的工具调用仍从 1 开始，避免统计唯一键冲突。
        var callIndex = 0;
        foreach (var toolCall in toolCalls)
        {
            callIndex = isPreprocessing ? 0 : callIndex + 1;
            cancellationToken.ThrowIfCancellationRequested();
            using var toolScope = logger.BeginScope("工具={ToolName}，调用ID={ToolCallId}", toolCall.Name, toolCall.Id);

            string content;
            if (toolDefinitions.TryGetValue(toolCall.Name, out var toolDefinition) == false ||
                (isPreprocessing == false && toolDefinition.Enabled == false))
            {
                content = $"工具未启用或未配置：{toolCall.Name}";
                logger.LogWarning("工具调用已跳过：{Reason}", content);
            }
            else
            {
                if (tools.TryGetValue(toolCall.Name, out var tool) == false)
                {
                    content = $"未加载工具实现：{toolCall.Name}";
                    logger.LogWarning("工具调用已跳过：{Reason}", content);
                }
                else
                {
                    logger.LogInformation("开始执行工具。");
                    var toolCallId = await statistics.StartToolCallAsync(requestId, modelCallId, toolCall.Id, callIndex, toolCall.Name, cancellationToken);
                    var startedAt = Stopwatch.GetTimestamp();
                    var status = "failed";
                    var errorType = "";
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(toolDefinition.TimeoutSeconds));
                    try
                    {
                        // 工具未响应取消时也结束等待；工具自身仍需配合取消，才能停止实际工作。
                        content = await tool.ExecuteAsync(toolCall.ArgumentsJson, context, timeout.Token).WaitAsync(timeout.Token);
                        status = "completed";
                        logger.LogInformation("工具执行完成：结果字符数={ContentLength}，耗时={ElapsedMilliseconds:F0} ms",
                            content?.Length ?? 0, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                    }
                    catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
                    {
                        status = "cancelled";
                        errorType = exception.GetType().Name;
                        // 整个行为被主动取消，不继续执行了
                        logger.LogInformation("工具执行已取消：耗时={ElapsedMilliseconds:F0} ms", Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                        throw;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == false && timeout.IsCancellationRequested)
                    {
                        status = "timeout";
                        errorType = nameof(OperationCanceledException);
                        logger.LogWarning("工具执行超时：上限={TimeoutSeconds} s，耗时={ElapsedMilliseconds:F0} ms", toolDefinition.TimeoutSeconds, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                        content = $"工具执行超时：{toolCall.Name}，已超过 {toolDefinition.TimeoutSeconds} 秒";
                    }
                    catch (Exception exception) when (cancellationToken.IsCancellationRequested == false)
                    {
                        status = "failed";
                        errorType = exception.GetType().Name;
                        logger.LogWarning(exception, "工具执行失败：耗时={ElapsedMilliseconds:F0} ms", Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                        content = $"工具执行失败：{toolCall.Name}，{exception.Message}";
                    }
                    finally
                    {
                        await statistics.FinishToolCallAsync(toolCallId, status,
                            (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, errorType, CancellationToken.None);
                    }
                }
            }

            messages.Add(new ChatMessage
            {
                Role = ChatRole.Tool,
                ToolCallId = toolCall.Id,
                ContentList = [new ChatTextContent { Text = content }]
            });
        }

        return messages;
    }
    #endregion
}
