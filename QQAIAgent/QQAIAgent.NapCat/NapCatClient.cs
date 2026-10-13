using System.Globalization;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using QQAIAgent.Messaging;
using QQAIAgent.Messaging.Content;
using QQAIAgent.NapCat.Config;

namespace QQAIAgent.NapCat;

public class NapCatClient(NapCatConfig config, ILogger<NapCatClient> logger) : IMessageSender, IMessageReader, IDisposable
{
    #region fields
    private readonly ClientWebSocket _socket = new();
    private readonly byte[] _receiveBuffer = new byte[8192];
    private readonly CancellationTokenSource _receiveCancellation = new();
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private readonly object _responseLock = new();
    private Task? _receiveTask;
    private string? _pendingEcho;
    private TaskCompletionSource<JsonElement>? _pendingResponse;
    private bool _isdisposed;
    #endregion

    #region events
    /// <summary>
    /// 发布群消息。处理器在接收循环中运行，不能阻塞等待 API 响应。
    /// </summary>
    public event Action<GroupMessage>? GroupMessageReceived;
    #endregion

    #region methods
    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(config.AccessToken) == false)
        {
            _socket.Options.SetRequestHeader("Authorization", $"Bearer {config.AccessToken}");
        }

        await _socket.ConnectAsync(new Uri(config.WebSocketUrl), cancellationToken);
        // The receive loop has its own lifetime; the connection timeout must not cancel it after startup.
        _receiveTask = ReceiveLoopAsync(_receiveCancellation.Token);
    }

    public async Task VerifyAccountAsync(CancellationToken cancellationToken)
    {
        var data = await CallApiAsync("get_login_info", new { }, cancellationToken);
        var account = data.GetProperty("user_id").GetInt64();
        if (account != config.BotAccount)
        {
            throw new InvalidOperationException("NapCat 登录账号与配置的机器人账号不一致。");
        }
    }

    public async Task<long> SendGroupMessageAsync(long groupId, long userId, string text, long? quoteMessageId, bool atUser, CancellationToken cancellationToken)
    {
        // 优先引用提问消息：引用内容自带提问时的艾特，仅在提问不带艾特或无法引用时才显式艾特。
        var segments = new List<object>();
        if (quoteMessageId is long quotedMessageId)
        {
            segments.Add(new { type = "reply", data = new { id = quotedMessageId } });
        }

        if (atUser)
        {
            segments.Add(new { type = "at", data = new { qq = userId.ToString(CultureInfo.InvariantCulture) } });
        }

        segments.Add(new { type = "text", data = new { text = $" {text}" } });

        var data = await CallApiAsync("send_group_msg", new
        {
            group_id = groupId,
            message = segments
        }, cancellationToken);
        return data.GetProperty("message_id").GetInt64();
    }

    public async Task<GroupMessage> GetGroupMessageAsync(long messageId, CancellationToken cancellationToken)
    {
        var data = await CallApiAsync("get_msg", new { message_id = messageId }, cancellationToken);
        if (data.GetProperty("message_type").GetString() != "group")
        {
            throw new InvalidDataException("引用消息必须是群消息。");
        }

        return ParseGroupMessage(data);
    }

    public async Task<IReadOnlyList<GroupMessage>> GetForwardMessagesAsync(string resId, CancellationToken cancellationToken)
    {
        // 兼容 message_id 与 id 两种参数名，一并传入。
        var data = await CallApiAsync("get_forward_msg", new { message_id = resId, id = resId }, cancellationToken);
        var messages = new List<GroupMessage>();
        if (data.TryGetProperty("messages", out var nodes) && nodes.ValueKind == JsonValueKind.Array)
        {
            // 转发节点结构与群消息事件同构，直接按群消息解析。
            messages.AddRange(nodes.EnumerateArray().Select(ParseGroupMessage));
        }

        return messages;
    }

    public async Task WaitForDisconnectAsync(CancellationToken cancellationToken)
    {
        var receiveTask = _receiveTask ?? throw new InvalidOperationException("NapCat 尚未建立连接。");
        await receiveTask.WaitAsync(cancellationToken);
    }

    public async Task DisconnectAsync()
    {
        // The stopping token may already be canceled; use a separate timeout for the closing handshake.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(config.DisconnectTimeoutSeconds));
        try
        {
            await _requestLock.WaitAsync(timeout.Token);
            try
            {
                if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    // The receive loop reads the peer's close frame; do not start a second receive operation here.
                    await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Stopping", timeout.Token);
                }
            }
            finally
            {
                _requestLock.Release();
            }

            if (_receiveTask is not null)
            {
                await _receiveTask.WaitAsync(timeout.Token);
            }
        }
        catch (OperationCanceledException)
        {
            _socket.Abort();
        }
        catch (WebSocketException)
        {
            _socket.Abort();
        }
        finally
        {
            _receiveCancellation.Cancel();
            if (_receiveTask is not null)
            {
                try
                {
                    await _receiveTask;
                }
                catch (OperationCanceledException)
                {
                    // Canceling the receive loop is part of shutdown.
                }
                catch (WebSocketException) when (_socket.State == WebSocketState.Aborted)
                {
                    // An aborted closing handshake also aborts the pending receive operation.
                }
            }
        }
    }

    public void Dispose()
    {
        if (_isdisposed)
        {
            return;
        }

        _isdisposed = true;
        _receiveCancellation.Cancel();
        _socket.Dispose();
        _receiveCancellation.Dispose();
        _requestLock.Dispose();
    }

    private async Task<JsonElement> CallApiAsync(string action, object parameters, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _receiveCancellation.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(config.ApiTimeoutSeconds));

        var acquired = false;
        try
        {
            await _requestLock.WaitAsync(timeout.Token);
            acquired = true;
            if (_socket.State != WebSocketState.Open)
            {
                throw new IOException("NapCat 连接已关闭，无法调用接口。");
            }

            var echo = Guid.NewGuid().ToString("N");
            var response = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_responseLock)
            {
                _pendingEcho = echo;
                _pendingResponse = response;
            }

            if (_receiveTask is { IsCompleted: true })
            {
                await _receiveTask;
                throw new IOException("NapCat 接收循环已结束，无法调用接口。");
            }

            var request = JsonSerializer.SerializeToUtf8Bytes(new
            {
                action,
                @params = parameters,
                echo
            });
            await _socket.SendAsync(request.AsMemory(), WebSocketMessageType.Text, true, timeout.Token);

            var root = await response.Task.WaitAsync(timeout.Token);
            var retcode = root.GetProperty("retcode").GetInt32();
            if (root.GetProperty("status").GetString() != "ok" || retcode != 0)
            {
                throw new InvalidOperationException($"NapCat 接口 {action} 调用失败，retcode={retcode}。");
            }

            return root.GetProperty("data");
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested == false
            && _receiveCancellation.IsCancellationRequested == false)
        {
            throw new TimeoutException($"NapCat 接口 {action} 调用超时（{config.ApiTimeoutSeconds} 秒）。");
        }
        finally
        {
            if (acquired)
            {
                lock (_responseLock)
                {
                    _pendingEcho = null;
                    _pendingResponse = null;
                }

                _requestLock.Release();
            }
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            while (cancellationToken.IsCancellationRequested == false)
            {
                JsonDocument? message;
                try
                {
                    message = await ReceiveMessageAsync(cancellationToken);
                }
                catch (JsonException exception) when (cancellationToken.IsCancellationRequested == false)
                {
                    // 已收完当前消息的全部分片，JSON 无效时可以直接接收下一条。
                    logger.LogWarning(exception, "NapCat 消息 JSON 解析失败，已跳过当前消息。");
                    continue;
                }

                using (message)
                {
                    if (message is null)
                    {
                        return;
                    }

                    try
                    {
                        var root = message.RootElement;
                        // API responses and events share this connection; only complete the request with a matching echo.
                        if (root.TryGetProperty("echo", out var echo) && echo.ValueKind == JsonValueKind.String)
                        {
                            lock (_responseLock)
                            {
                                if (echo.GetString() == _pendingEcho)
                                {
                                    // The document is disposed after dispatch, so the pending request needs its own JSON data.
                                    _pendingResponse?.TrySetResult(root.Clone());
                                }
                            }

                            continue;
                        }

                        if (IsGroupMessageEvent(root))
                        {
                            var groupMessage = ParseGroupMessage(root);
                            GroupMessageReceived?.Invoke(groupMessage);
                        }
                    }
                    catch (Exception exception) when (cancellationToken.IsCancellationRequested == false)
                    {
                        // 单条消息的字段解析或回调失败，不影响后续消息和 API 响应的接收。
                        logger.LogWarning(exception, "NapCat 消息解析或分发失败，已跳过当前消息。");
                    }
                }
            }
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            lock (_responseLock)
            {
                _pendingResponse?.TrySetException(failure ?? new IOException("NapCat 在接口响应返回前关闭了连接。"));
            }
        }
    }

    private static bool IsGroupMessageEvent(JsonElement root)
    {
        // message_sent 是机器人自身消息的上报，同样触发记录，再由处理端排除自身消息。
        return root.TryGetProperty("post_type", out var postType)
            && postType.GetString() is "message" or "message_sent"
            && root.GetProperty("message_type").GetString() == "group";
    }

    // 群消息事件与 get_msg 响应共用内容解析；API 响应不依赖事件的 post_type。
    private static GroupMessage ParseGroupMessage(JsonElement root)
    {
        var message = root.GetProperty("message");
        root.TryGetProperty("sender", out var sender);
        var groupMessage = new GroupMessage
        {
            // 事件与 get_msg 响应都带消息 ID，用于回复时引用提问消息。
            MessageId = root.TryGetProperty("message_id", out var messageId) && messageId.ValueKind != JsonValueKind.Null
                ? ReadMessageInteger(messageId)
                : null,
            MessageTime = root.TryGetProperty("time", out var time) && time.ValueKind != JsonValueKind.Null
                ? ReadMessageInteger(time)
                : null,
            GroupId = root.TryGetProperty("group_id", out var groupId) ? ReadMessageInteger(groupId) : 0,
            SenderId = root.TryGetProperty("user_id", out var userId) ? ReadMessageInteger(userId) : 0,
            SenderNickname = sender.ValueKind == JsonValueKind.Object && sender.TryGetProperty("nickname", out var nickname)
                ? nickname.GetString() ?? ""
                : "",
            SenderCard = sender.ValueKind == JsonValueKind.Object && sender.TryGetProperty("card", out var card)
                ? card.GetString() ?? ""
                : "",
            SubType = root.TryGetProperty("sub_type", out var subType) ? subType.GetString() ?? "" : "",
            ContentJson = message.GetRawText()
        };
        // napcat 配置决定是发来原始文本消息还是已经解析好的数组
        if (message.ValueKind == JsonValueKind.String)
        {
            ParseCqMessage(message.GetString() ?? "", groupMessage);
        }
        else if (message.ValueKind == JsonValueKind.Array)
        {
            foreach (var segment in message.EnumerateArray())
            {
                AppendMessageContent(groupMessage, segment.GetProperty("type").GetString() ?? "", segment.GetProperty("data"));
            }
        }
        else
        {
            throw new InvalidDataException("NapCat 群消息内容必须是字符串或消息段数组。");
        }

        return groupMessage;
    }

    private static void AppendMessageContent(GroupMessage message, string type, JsonElement data)
    {
        switch (type)
        {
            case "text":
                message.MessageContentList.Add(new TextContent
                {
                    Text = data.GetProperty("text").GetString() ?? ""
                });
                break;
            case "at":
                message.MessageContentList.Add(new AtContent
                {
                    // qq 可以是数字或字符串，"all" 表示全体成员。
                    Target = data.GetProperty("qq").ToString()
                });
                break;
            case "face":
                {
                    var faceId = ReadMessageInteger(data.GetProperty("id"));
                    message.MessageContentList.Add(new FaceContent
                    {
                        Id = faceId,
                        // 未知 ID 保留空名称，由处理端降级显示。
                        Name = QqFaceTable.GetName(faceId)
                    });
                }
                break;
            case "mface":
                {
                    // 商城表情本质是表情包图片，按图片处理；summary 是 QQ 官方的表情描述文字。
                    message.MessageContentList.Add(new ImageContent
                    {
                        Url = data.TryGetProperty("url", out var mfaceUrl) ? mfaceUrl.GetString() ?? "" : "",
                        Summary = data.TryGetProperty("summary", out var mfaceSummary) ? mfaceSummary.GetString() ?? "" : ""
                    });
                }
                break;
            case "json":
                message.MessageContentList.Add(ParseJsonCard(data));
                break;
            case "forward":
                // 合并转发内容需在处理阶段调 get_forward_msg 展开，这里只保留资源 ID。
                message.MessageContentList.Add(new ForwardContent { ResourceId = data.GetProperty("id").ToString() });
                break;
            case "image":
                message.MessageContentList.Add(new ImageContent
                {
                    File = data.GetProperty("file").GetString() ?? "",
                    Url = data.TryGetProperty("url", out var url) ? url.GetString() ?? "" : "",
                    Summary = data.TryGetProperty("summary", out var summary) ? summary.GetString() ?? "" : "",
                    SubType = data.TryGetProperty("sub_type", out var subType) && subType.ValueKind != JsonValueKind.Null
                        ? (ImageSubType)ReadMessageInteger(subType)
                        : ImageSubType.Normal,
                    FileSize = data.TryGetProperty("file_size", out var fileSize) && fileSize.ValueKind != JsonValueKind.Null
                        ? ReadMessageInteger(fileSize)
                        : 0
                });
                break;
            case "reply":
                // 引用是上下文关系，不加入当前用户的正文消息段。
                message.RefMessageId = ReadMessageInteger(data.GetProperty("id"));
                break;
            default:
                message.MessageContentList.Add(new UnknownContent
                {
                    OriginalType = type,
                    DataJson = data.GetRawText()
                });
                break;
        }
    }

    // json 卡段的 data 字段是再编码一层的 JSON 字符串，常见结构为 prompt 摘要加 meta 下首个子对象的 title/desc/qqdocurl。
    private static JsonCardContent ParseJsonCard(JsonElement data)
    {
        var card = new JsonCardContent();
        JsonElement root;
        try
        {
            root = data.ValueKind == JsonValueKind.String
                ? JsonSerializer.Deserialize<JsonElement>(data.GetString() ?? "")
                : data;
        }
        catch (JsonException)
        {
            // 内层 JSON 无效时保留空卡片，由处理端降级显示。
            return card;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            return card;
        }

        card.Prompt = ReadOptionalString(root, "prompt");
        if (root.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object)
        {
            // 卡片类型不同，meta 下的子对象名也不同（detail_1/detail_2/news 等），取首个对象。
            foreach (var detail in meta.EnumerateObject())
            {
                if (detail.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                card.Title = ReadOptionalString(detail.Value, "title");
                card.Description = ReadOptionalString(detail.Value, "desc");
                card.Url = ReadOptionalString(detail.Value, "qqdocurl");
                break;
            }
        }

        return card;
    }

    private static string ReadOptionalString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    private static void ParseCqMessage(string text, GroupMessage message)
    {
        var position = 0;
        // 先识别未转义的 CQ 码，再解码文本，避免把用户输入的字面 CQ 码当成消息段。
        foreach (Match match in Regex.Matches(text, @"\[CQ:(?<type>[a-zA-Z0-9_]+)(?<data>(?:,[a-zA-Z0-9_]+=[^,\]]*)*)\]"))
        {
            if (match.Index > position)
            {
                message.MessageContentList.Add(new TextContent
                {
                    Text = DecodeCqText(text[position..match.Index])
                });
            }

            var data = new Dictionary<string, string>();
            foreach (var parameter in match.Groups["data"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = parameter.IndexOf('=');
                // 参数中的逗号需单独解码，且必须先于 &amp; 解码，避免重复解码。
                data[parameter[..separator]] = DecodeCqText(parameter[(separator + 1)..].Replace("&#44;", ","));
            }

            AppendMessageContent(message, match.Groups["type"].Value, JsonSerializer.SerializeToElement(data));
            position = match.Index + match.Length;
        }

        if (position < text.Length)
        {
            message.MessageContentList.Add(new TextContent
            {
                Text = DecodeCqText(text[position..])
            });
        }
    }

    private static string DecodeCqText(string text)
    {
        // 先解码方括号，最后解码 &amp;，避免转义后的实体文本被解码两次。
        return text.Replace("&#91;", "[").Replace("&#93;", "]").Replace("&amp;", "&");
    }

    private static long ReadMessageInteger(JsonElement value)
    {
        // 数组消息可能使用 JSON 数字，CQ 码参数则统一是字符串。
        return value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : long.Parse(value.GetString() ?? "", CultureInfo.InvariantCulture);
    }

    private async Task<JsonDocument?> ReceiveMessageAsync(CancellationToken cancellationToken)
    {
        using var message = new MemoryStream();
        ValueWebSocketReceiveResult result;
        do
        {
            result = await _socket.ReceiveAsync(_receiveBuffer.AsMemory(), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                throw new InvalidDataException("NapCat 返回了非文本 WebSocket 消息。");
            }

            // WebSocket messages may span multiple frames; receive the complete message before parsing JSON.
            message.Write(_receiveBuffer, 0, result.Count);
        }
        while (result.EndOfMessage == false);

        message.Position = 0;
        return await JsonDocument.ParseAsync(message, cancellationToken: cancellationToken);
    }
    #endregion
}

