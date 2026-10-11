using System.Diagnostics;
using Microsoft.Extensions.Logging;
using QQAIAgent.Abstractions.Tool;
using QQAIAgent.Agent.Chat;
using QQAIAgent.Agent.Config;
using QQAIAgent.Messaging;
using QQAIAgent.Messaging.Content;

namespace QQAIAgent.Agent;

public class GroupMessageAgent(AgentConfig config, long botAccount, IMessageSender messageSender, IChatModel chatModel, IReadOnlyList<ChatToolDefinition> toolDefinitions, ToolExecutor toolExecutor, ILogger<GroupMessageAgent> logger)
{
    #region fields
    #endregion
    
    #region methods
    /// <summary>处理群消息，通过模型与工具的多轮调用生成最终回复，返回回复消息 ID。</summary>
    public async Task<long?> HandleMessageAsync(GroupMessage message, CancellationToken cancellationToken)
    {
        using var requestScope = logger.BeginScope("群={GroupId}，发送者={SenderId}", message.GroupId, message.SenderId);

        // 消息接收端已筛选出需要处理的消息，并匹配了 "123" 前缀。
        // 当前模型接口接收纯文本，按消息顺序提取文本段；其他消息段保留在原消息中。
        var text = string.Concat(message.MessageContentList
            .OfType<TextContent>()
            .Select(content => content.Text)).TrimStart()[3..].Trim();
        // 消息列表只属于本次请求，不与其他用户或后续请求共享上下文。
        var messages = new List<ChatMessage>();
        // 系统提示词放在最前面，并随工具调用消息保留到本次请求结束。
        if (string.IsNullOrWhiteSpace(config.SystemPrompt) == false)
        {
            messages.Add(new ChatMessage { Role = ChatRole.System, Content = config.SystemPrompt });
        }
        messages.Add(new ChatMessage { Role = ChatRole.User, Content = text });

        // 强度只属于本条用户消息；工具未调整时沿用上一轮，不影响其他并发请求。
        var toolContext = new ToolContext();

        for (var modelCall = 0; modelCall < config.MaxModelCalls; modelCall++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var roundScope = logger.BeginScope("轮次={ModelCall}/{MaxModelCalls}", modelCall + 1, config.MaxModelCalls);

            logger.LogInformation("开始请求模型：消息数={MessageCount}，思考强度={ReasoningEffort}", messages.Count, toolContext.ReasoningEffort);
            var startedAt = Stopwatch.GetTimestamp();
            ChatResponse response;
            try
            {
                response = await chatModel.CompleteAsync(new ChatRequest
                {
                    Messages = messages,
                    Tools = chatModel.SupportsToolCalling ? toolDefinitions : [],
                    ReasoningEffort = toolContext.ReasoningEffort
                }, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation("模型请求已取消：耗时={ElapsedMilliseconds:F0} ms", Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                throw;
            }
            catch (Exception exception) when (cancellationToken.IsCancellationRequested == false)
            {
                logger.LogWarning("模型请求失败：超时={IsTimeout}，耗时={ElapsedMilliseconds:F0} ms，原因={Reason}",
                    exception is OperationCanceledException, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, exception.Message);
                var failureText = exception is OperationCanceledException
                    ? "模型请求超时，请稍后再试"
                    : "模型请求失败，请稍后再试";
                await messageSender.SendGroupMessageAsync(message.GroupId, failureText, cancellationToken);
                // 提示发送后继续抛出原异常，由宿主记录日志并处理下一条消息。
                throw;
            }

            logger.LogInformation("模型请求完成：结束原因={FinishReason}，工具调用数={ToolCallCount}，回复字符数={ContentLength}，耗时={ElapsedMilliseconds:F0} ms",
                response.FinishReason, response.Message.ToolCalls.Count, response.Message.Content?.Length ?? 0,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

            if (response.FinishReason != ChatFinishReason.ToolCalls)
            {
                if (string.IsNullOrWhiteSpace(response.Message.Content))
                {
                    logger.LogWarning("模型回复内容为空，将发送空回复提示。");
                    return await messageSender.SendGroupMessageAsync(message.GroupId, "回复内容为空", cancellationToken);
                }

                return await messageSender.SendGroupMessageAsync(message.GroupId, response.Message.Content, cancellationToken);
            }

            // 已无下一次模型调用机会时停止，避免执行无法继续交给模型的工具调用。
            if (modelCall + 1 >= config.MaxModelCalls)
            {
                break;
            }

            // 先保留完整的 assistant 工具调用消息，再按调用 ID 追加全部工具结果。
            messages.Add(response.Message);
            var toolMessages = await toolExecutor.ExecuteAsync(response.Message.ToolCalls, toolContext, cancellationToken);
            messages.AddRange(toolMessages);
        }

        logger.LogWarning("已达到模型调用次数上限：上限={MaxModelCalls}", config.MaxModelCalls);
        return await messageSender.SendGroupMessageAsync(message.GroupId, "已达到模型调用次数上限，请重新发起请求", cancellationToken);
    }
    #endregion
}
