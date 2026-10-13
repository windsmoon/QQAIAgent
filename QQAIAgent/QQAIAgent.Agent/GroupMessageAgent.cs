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
        // 先展开正文里的合并转发，再按原顺序转换文本、图片和视频。
        await TryExpandForwardsAsync(message.MessageContentList, 1, cancellationToken);
        var chatContentList = new List<ChatContent>();
        await AppendChatContentsAsync(chatContentList, message.MessageContentList, message.GroupId, isReference: false, cancellationToken);
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
                (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds, "", CancellationToken.None,
                promptTokens: response.PromptTokens, completionTokens: response.CompletionTokens,
                totalTokens: response.TotalTokens, promptCacheHitTokens: response.PromptCacheHitTokens,
                promptCacheMissTokens: response.PromptCacheMissTokens, reasoningTokens: response.ReasoningTokens);

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
        var usage = await statistics.GetRequestTokenUsageAsync(requestId, CancellationToken.None);
        // 缓存只命中输入 Token，命中率不使用包含输出 Token 的总用量作为分母。
        var cacheHitRate = usage.PromptTokens is > 0 && usage.PromptCacheHitTokens.HasValue
            ? $"{usage.PromptCacheHitTokens.Value * 100d / usage.PromptTokens.Value:F2}%"
            : "未知";
        logger.LogInformation("本次请求 Token 用量：总输入 Token={PromptTokens}，总输出 Token={CompletionTokens}，缓存命中率={CacheHitRate}",
            usage.PromptTokens?.ToString() ?? "未知", usage.CompletionTokens?.ToString() ?? "未知", cacheHitRate);
        return messageId;
    }

    private async Task AppendChatContentsAsync(List<ChatContent> chatContentList, List<MessageContent> messageContentList, long groupId, bool isReference, CancellationToken cancellationToken)
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
                case VideoContent videoContent:
                    {
                        // 主模型通过工具理解视频，保留来源提供的完整地址，避免改写带签名的 URL。
                        var videoUrl = videoContent.Url != "" ? videoContent.Url : videoContent.File;
                        var hasVideoUrl = Uri.TryCreate(videoUrl, UriKind.Absolute, out var videoUri) &&
                            (videoUri.Scheme == Uri.UriSchemeHttp || videoUri.Scheme == Uri.UriSchemeHttps);
                        if (hasVideoUrl == false && videoContent.FileId != "") // 没有 url 但是 fileId 不为空
                        {
                            var sourceGroupId = videoContent.SourceGroupId != 0 ? videoContent.SourceGroupId : groupId;
                            try
                            {
                                // 群文件只有资源 ID 时在处理阶段获取直链，接收循环需继续运行以接收接口响应。
                                videoUrl = await messageReader.GetGroupFileUrlAsync(sourceGroupId, videoContent.FileId, cancellationToken);
                                hasVideoUrl = Uri.TryCreate(videoUrl, UriKind.Absolute, out videoUri) &&
                                    (videoUri.Scheme == Uri.UriSchemeHttp || videoUri.Scheme == Uri.UriSchemeHttps);
                            }
                            catch (Exception exception) when (cancellationToken.IsCancellationRequested == false)
                            {
                                logger.LogWarning(exception, "获取视频群文件下载地址失败：群={GroupId}，文件ID={FileId}", sourceGroupId, videoContent.FileId);
                                chatContentList.Add(new ChatTextContent { Text = $"[视频:{videoContent.File}，获取下载地址失败]" });
                                break;
                            }
                        }

                        chatContentList.Add(new ChatTextContent
                        {
                            Text = hasVideoUrl
                                ? $"[视频]（地址：{videoUrl}；需要理解画面和声音时调用 understand_video 工具）"
                                : "[视频：未提供 HTTP/HTTPS 视频地址，当前无法理解视频内容]"
                        });
                    }
                    break;
                case AtContent atContent:
                    chatContentList.Add(new ChatTextContent
                    {
                        Text = atContent.Target == "all" ? "@全体成员" : $"@{atContent.Target}"
                    });
                    break;
                case FaceContent faceContent:
                    chatContentList.Add(new ChatTextContent
                    {
                        // 表情按名称转成文字标记；未知 ID 时退化为通用标记。
                        Text = faceContent.Name == "" ? "[表情]" : $"[表情:{faceContent.Name}]"
                    });
                    break;
                case JsonCardContent jsonCardContent:
                    {
                        // 卡片转成文字标记：官方摘要与标题、描述拼接，跳转链接随后补充。
                        var summary = string.Join(" - ", new[] { jsonCardContent.Prompt, jsonCardContent.Title, jsonCardContent.Description }.Where(part => part != ""));
                        var url = jsonCardContent.Url != "" ? $"（链接：{jsonCardContent.Url}）" : "";
                        chatContentList.Add(new ChatTextContent
                        {
                            Text = summary == "" ? $"[卡片消息]{url}" : $"[卡片:{summary}]{url}"
                        });
                    }
                    break;
                case ForwardContent:
                    // 超出展开深度上限（或关闭展开）而保留的转发，降级为省略标记。
                    chatContentList.Add(new ChatTextContent { Text = "[更深层转发内容已省略]" });
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

    // 展开列表中的合并转发：节点拼回为标签文本加原消息段，图片等内容保持原类型继续走常规转换。
    private async Task TryExpandForwardsAsync(List<MessageContent> contents, int depth, CancellationToken cancellationToken)
    {
        if (depth > config.MaxForwardDepth)
        {
            return;
        }

        for (var i = 0; i < contents.Count; i++)
        {
            if (contents[i] is not ForwardContent forward)
            {
                continue;
            }

            var expanded = new List<MessageContent>();
            try
            {
                var nodes = await messageReader.GetForwardMessagesAsync(forward.ResourceId, cancellationToken);
                var limitedMessageList = nodes.Take(config.MaxForwardMessagesPerLayer).ToList();
                expanded.Add(new TextContent
                {
                    Text = $"[合并转发消息，共 {nodes.Count} 条{(limitedMessageList.Count < nodes.Count ? $"，仅展开前 {limitedMessageList.Count} 条" : "")}]\n"
                });
                foreach (var node in limitedMessageList)
                {
                    var name = node.SenderCard != "" ? node.SenderCard : node.SenderNickname;
                    expanded.Add(new TextContent { Text = $"{name}({node.SenderId})：" });
                    expanded.AddRange(node.MessageContentList);
                }
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested == false)
            {
                // 获取失败不中断整个请求，降级为文字标记。
                expanded.Add(new TextContent { Text = "[合并转发消息，获取失败]" });
            }

            await TryExpandForwardsAsync(expanded, depth + 1, cancellationToken);
            // 删除这个 forward 数据，改用展开后的数据
            contents.RemoveAt(i);
            contents.InsertRange(i, expanded);
            i += expanded.Count - 1; //让循环跳过刚才插入的内容，防止重复处理
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

            // 引用消息里同样可能包含合并转发，取回后先展开再转换。
            await TryExpandForwardsAsync(refMessage.MessageContentList, 0, cancellationToken);
            // 引用说明作为独立文本段放在正文之前，图片保留在原位置。
            var contentList = new List<ChatContent>();
            contentList.Add(new ChatTextContent
            {
                Text = $"以下是当前用户第 {referenceDepth + 1} 层引用的消息，仅作为上下文：\n发送者 QQ：{refMessage.SenderId}\n引用内容：\n"
            });
            await AppendChatContentsAsync(contentList, refMessage.MessageContentList,
                refMessage.GroupId != 0 ? refMessage.GroupId : groupMessage.GroupId, isReference: true, cancellationToken);
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
