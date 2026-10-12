using System.Diagnostics;
using Microsoft.Extensions.Logging;
using QQAIAgent.Abstractions.Tool;
using QQAIAgent.Agent.Chat;
using QQAIAgent.Agent.Chat.Content;
using QQAIAgent.Agent.Config;
using QQAIAgent.Agent.Statistics;
using QQAIAgent.Messaging;
using QQAIAgent.Messaging.Content;

namespace QQAIAgent.Agent;

public class GroupMessageAgent(AgentConfig config, long botAccount, IMessageSender messageSender, IMessageReader messageReader, IChatModel chatModel, IReadOnlyList<ChatToolDefinition> toolDefinitions, ToolExecutor toolExecutor, RequestStatistics statistics, ILogger<GroupMessageAgent> logger)
{
    #region fields
    #endregion
    
    #region methods
    /// <summary>处理群消息，通过模型与工具的多轮调用生成最终回复，返回回复消息 ID。</summary>
    /// <param name="receivedTimeStamp">通过 Stopwatch.GetTimestamp 记录的请求接收时间，用于累计排队与处理时长。</param>
    public async Task<long?> HandleMessageAsync(GroupMessage message, long receivedTimeStamp, CancellationToken cancellationToken)
    {
        var requestId = await statistics.StartRequestAsync(botAccount, message.GroupId, message.SenderId, cancellationToken);
        using var requestScope = logger.BeginScope("请求={RequestId}，群={GroupId}，发送者={SenderId}", requestId, message.GroupId, message.SenderId);
        var startedAt = receivedTimeStamp;
        var status = "failed";
        var errorType = "";
        // 从接收时刻累计总时长，排队会消耗时限，各轮模型和工具调用不会重置计时。
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var remainingTime = TimeSpan.FromSeconds(config.QQRequestTimeoutSeconds) - Stopwatch.GetElapsedTime(startedAt);
        if (remainingTime <= TimeSpan.Zero)
        {
            timeout.Cancel();
        }
        else
        {
            timeout.CancelAfter(remainingTime);
        }
        try
        {
            timeout.Token.ThrowIfCancellationRequested();
            var result = await HandleRequestAsync(message, requestId, timeout.Token);
            status = result.Status;
            errorType = result.ErrorType;
            return result.MessageId;
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            status = "cancelled";
            errorType = exception.GetType().Name;
            throw;
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested && cancellationToken.IsCancellationRequested == false)
        {
            status = "timeout";
            errorType = exception.GetType().Name;
            logger.LogWarning("用户请求处理超时：上限={TimeoutSeconds} s，耗时={ElapsedMilliseconds:F0} ms",
                config.QQRequestTimeoutSeconds, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            // 总时限已取消，超时提示使用宿主令牌；宿主退出时仍会取消发送。
            return await SendReplyAsync(requestId, message, "请求处理超时，请稍后再试", cancellationToken);
        }
        catch (Exception exception)
        {
            errorType = exception.GetType().Name;
            throw;
        }
        finally
        {
            // 请求令牌可能已经取消，结束状态仍需写入；只保存异常类型，不保存异常正文。
            await statistics.FinishRequestAsync(requestId, status,
                (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, errorType, CancellationToken.None);
        }
    }

    private async Task<(long? MessageId, string Status, string ErrorType)> HandleRequestAsync(GroupMessage message, string requestId, CancellationToken cancellationToken)
    {
        // 消息接收端已筛选出需要处理的消息，并移除了触发标记。
        // 按原顺序转换文本和图片，不跨图片拼接或裁剪文本段。
        var chatContentList = new List<ChatContent>();
        AppendChatContents(chatContentList, message.MessageContentList, isReference: false);
        // 消息列表只属于本次请求，不与其他用户或后续请求共享上下文。
        var messages = new List<ChatMessage>();
        // 系统提示词放在最前面，并随工具调用消息保留到本次请求结束。
        if (string.IsNullOrWhiteSpace(config.SystemPrompt) == false)
        {
            messages.Add(new ChatMessage { Role = ChatRole.System, ContentList = [new ChatTextContent { Text = config.SystemPrompt }] });
        }

        // 处理引用
        var referenceResult = await ParseReferenceAsync(messages, message, requestId, cancellationToken);
        if (referenceResult.Success == false)
        {
            return (null, "reference_failed", referenceResult.ErrorType);
        }

        messages.Add(new ChatMessage { Role = ChatRole.User, ContentList = chatContentList });

        // 强度只属于本条用户消息；工具未调整时沿用上一轮，不影响其他并发请求。
        var toolContext = new ToolContext();

        for (var modelCall = 0; modelCall < config.MaxModelCalls; modelCall++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var roundScope = logger.BeginScope("轮次={ModelCall}/{MaxModelCalls}", modelCall + 1, config.MaxModelCalls);

            logger.LogInformation("开始请求模型：消息数={MessageCount}，思考强度={ReasoningEffort}", messages.Count, toolContext.ReasoningEffort);
            var modelCallId = await statistics.StartModelCallAsync(requestId, modelCall + 1, toolContext.ReasoningEffort, cancellationToken);
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
            catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
            {
                await statistics.FinishModelCallAsync(modelCallId, "cancelled", "",
                    (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, exception.GetType().Name, CancellationToken.None);
                logger.LogInformation("模型请求已取消：耗时={ElapsedMilliseconds:F0} ms", Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                throw;
            }
            catch (Exception exception) when (cancellationToken.IsCancellationRequested == false)
            {
                await statistics.FinishModelCallAsync(modelCallId, exception is OperationCanceledException ? "timeout" : "failed", "",
                    (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, exception.GetType().Name, CancellationToken.None);
                logger.LogWarning("模型请求失败：超时={IsTimeout}，耗时={ElapsedMilliseconds:F0} ms，原因={Reason}",
                    exception is OperationCanceledException, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, exception.Message);
                var failureText = exception is OperationCanceledException
                    ? "模型请求超时，请稍后再试"
                    : "模型请求失败，请稍后再试";
                await SendReplyAsync(requestId, message, failureText, cancellationToken);
                // 提示发送后继续抛出原异常，由宿主记录日志并处理下一条消息。
                throw;
            }

            await statistics.FinishModelCallAsync(modelCallId, "completed", response.FinishReason.ToString(),
                (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, "", CancellationToken.None);

            var responseText = string.Concat(response.Message.ContentList
                .OfType<ChatTextContent>()
                .Select(content => content.Text));
            logger.LogInformation("模型请求完成：结束原因={FinishReason}，工具调用数={ToolCallCount}，回复字符数={ContentLength}，耗时={ElapsedMilliseconds:F0} ms",
                response.FinishReason, response.Message.ToolCalls.Count, responseText.Length,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

            if (response.FinishReason != ChatFinishReason.ToolCalls)
            {
                if (string.IsNullOrWhiteSpace(responseText))
                {
                    logger.LogWarning("模型回复内容为空，将发送空回复提示。");
                    return (await SendReplyAsync(requestId, message, "回复内容为空", cancellationToken), "empty_response", "");
                }

                return (await SendReplyAsync(requestId, message, responseText, cancellationToken), "completed", "");
            }

            // 已无下一次模型调用机会时停止，避免执行无法继续交给模型的工具调用。
            if (modelCall + 1 >= config.MaxModelCalls)
            {
                break;
            }

            // 先保留完整的 assistant 工具调用消息，再按调用 ID 追加全部工具结果。
            messages.Add(response.Message);
            var toolMessages = await toolExecutor.ExecuteAsync(response.Message.ToolCalls, toolContext, requestId, modelCallId, cancellationToken);
            messages.AddRange(toolMessages);
        }

        logger.LogWarning("已达到模型调用次数上限：上限={MaxModelCalls}", config.MaxModelCalls);
        return (await SendReplyAsync(requestId, message, "已达到模型调用次数上限，请重新发起请求", cancellationToken), "model_call_limit", "");
    }

    private async Task<long> SendReplyAsync(string requestId, GroupMessage message, string text, CancellationToken cancellationToken)
    {
        // 引用提问消息回复；提问不带艾特或无法引用时仍艾特发送者。
        var messageId = await messageSender.SendGroupMessageAsync(message.GroupId, message.SenderId, text, message.MessageId, message.NeedAtInReply, cancellationToken);
        await statistics.RecordReplyAsync(requestId, messageId, CancellationToken.None);
        return messageId;
    }

    private static void AppendChatContents(List<ChatContent> chatContentList, List<MessageContent> messageContentList, bool isReference)
    {
        foreach (var content in messageContentList)
        {
            switch (content)
            {
                case TextContent textContent:
                    chatContentList.Add(new ChatTextContent { Text = textContent.Text });
                    break;
                case ImageContent imageContent:
                    chatContentList.Add(new ChatImageContent { Url = imageContent.Url });
                    break;
                case AtContent atContent:
                    chatContentList.Add(new ChatTextContent
                    {
                        Text = atContent.Target == "all" ? "@全体成员" : $"@{atContent.Target}"
                    });
                    break;
                default:
                    // 沿用其他消息段的处理规则：引用保留文字标记，当前正文跳过。
                    if (isReference)
                    {
                        var text = content switch
                        {
                            UnknownContent unknownContent => $"[未支持的消息段：{unknownContent.OriginalType}]",
                            _ => $"[未支持的消息段：{content.ContentType}]"
                        };
                        chatContentList.Add(new ChatTextContent { Text = text });
                    }
                    break;
            }
        }
    }

    private async Task<(bool Success, string ErrorType)> ParseReferenceAsync(List<ChatMessage> messageList, GroupMessage groupMessage, string requestId, CancellationToken cancellationToken)
    {
        var referenceContextIndex = messageList.Count;
        var refMessageId = groupMessage.RefMessageId;
        for (int referenceDepth = 0; referenceDepth < config.MaxReferenceDepth && refMessageId is long currentRefMessageId; referenceDepth++)
        {
            GroupMessage refMessage;
            try
            {
                // 在处理阶段读取引用，接收循环必须保持运行才能返回 get_msg 的响应。
                refMessage = await messageReader.GetGroupMessageAsync(currentRefMessageId, cancellationToken);
            }
            catch (Exception exception) when (cancellationToken.IsCancellationRequested == false)
            {
                await SendReplyAsync(requestId, groupMessage, "获取引用消息失败，请稍后再试", cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                return (false, exception.GetType().Name);
                // logger.LogWarning("获取引用消息失败：消息ID={MessageId}，原因={Reason}", currentRefMessageId, exception.Message);
            }

            // 引用说明作为独立文本段放在正文之前，图片保留在原位置。
            var contentList = new List<ChatContent>();
            contentList.Add(new ChatTextContent
            {
                Text = $"以下是当前用户第 {referenceDepth + 1} 层引用的消息，仅作为上下文：\n发送者 QQ：{refMessage.SenderId}\n引用内容：\n"
            });
            AppendChatContents(contentList, refMessage.MessageContentList, isReference: true);
            // 每层都插到系统消息之后，让更早的引用排在前面；引用正文不处理触发前缀。
            messageList.Insert(referenceContextIndex, new ChatMessage
            {
                Role = ChatRole.User,
                ContentList = contentList
            });
            refMessageId = refMessage.RefMessageId;
        }

        return (true, "");
    }
    #endregion
}
