using System.Globalization;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using QQAIAgent.Infrastructure.Image;
using QQAIAgent.Infrastructure.Messages;
using QQAIAgent.Messaging;
using QQAIAgent.NapCat.Config;

namespace QQAIAgent.NapCat;

public class NapCatClient(NapCatConfig config, ForwardMessageStore forwardMessageStore, TextToImageStore textToImageStore, ILogger<NapCatClient> logger) : IMessageSender, IMessageReader, IDisposable
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

        try
        {
            var imageBytes = TextToImage.Render(text);
            segments.Add(new { type = "image", data = new { file = $"base64://{Convert.ToBase64String(imageBytes)}" } });
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "回复转图片失败，改为发送文本：群={GroupId}", groupId);
            segments.Add(new { type = "text", data = new { text = $" {text}" } });
        }

        // 保留原始回复文本，供自身图片消息上报后关联。
        var sendId = textToImageStore.BeginSend(groupId, text);
        long messageId;
        try
        {
            var data = await CallApiAsync("send_group_msg", new
            {
                group_id = groupId,
                message = segments
            }, cancellationToken);
            messageId = data.GetProperty("message_id").GetInt64();
        }
        catch
        {
            textToImageStore.CancelSend(sendId);
            throw;
        }

        try
        {
            // 发送已经成功，缓存写入不再受原请求取消影响，也不能将缓存失败当作发送失败。
            await textToImageStore.CompleteSendAsync(sendId, messageId, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "回复图片原文缓存写入失败：群={GroupId}，消息ID={MessageId}", groupId, messageId);
        }

        return messageId;
    }

    public async Task<GroupMessage> GetGroupMessageAsync(long messageId, CancellationToken cancellationToken)
    {
        var data = await CallApiAsync("get_msg", new { message_id = messageId }, cancellationToken);
        if (data.GetProperty("message_type").GetString() != "group")
        {
            throw new InvalidDataException("引用消息必须是群消息。");
        }

        var message = GroupMessageParser.ParseGroupMessage(data);
        await textToImageStore.RestoreAsync(message, cancellationToken);
        return message;
    }

    public async Task<IReadOnlyList<GroupMessage>> GetForwardMessagesAsync(string resId, CancellationToken cancellationToken)
    {
        // 合集内容不可变，先查本地缓存，未命中再调接口并落库。
        var dataJson = await forwardMessageStore.TryGetAsync(resId, cancellationToken);
        if (dataJson is null)
        {
            // 兼容 message_id 与 id 两种参数名，一并传入。
            var data = await CallApiAsync("get_forward_msg", new { message_id = resId, id = resId }, cancellationToken);
            dataJson = data.GetRawText();
            Task.Run(() => forwardMessageStore.SaveAsync(resId, dataJson, cancellationToken));
        }

        using var document = JsonDocument.Parse(dataJson);
        var messages = new List<GroupMessage>();
        if (document.RootElement.TryGetProperty("messages", out var nodes) && nodes.ValueKind == JsonValueKind.Array)
        {
            // 转发节点结构与群消息事件同构，直接按群消息解析。
            messages.AddRange(nodes.EnumerateArray().Select(GroupMessageParser.ParseGroupMessage));
        }

        foreach (var message in messages)
        {
            await textToImageStore.RestoreAsync(message, cancellationToken);
        }

        return messages;
    }

    public async Task<string> GetGroupFileUrlAsync(long groupId, string fileId, CancellationToken cancellationToken)
    {
        var data = await CallApiAsync("get_group_file_url", new { group_id = groupId, file_id = fileId }, cancellationToken);
        return ReadOptionalString(data, "url");
    }

    public async Task<List<(string Type, string Key, long ExpiresAt)>> GetRkeysAsync(CancellationToken cancellationToken)
    {
        var data = await CallApiAsync("get_rkey", new { }, cancellationToken);
        var keys = new List<(string Type, string Key, long ExpiresAt)>();
        foreach (var item in data.EnumerateArray())
        {
            var rkey = item.GetProperty("rkey").GetString() ?? string.Empty;
            if (rkey.StartsWith("&rkey=", StringComparison.Ordinal))
            {
                rkey = rkey[6..];
            }
            if (string.IsNullOrWhiteSpace(rkey))
            {
                throw new InvalidDataException("NapCat 返回的图片下载凭据为空。");
            }

            keys.Add((item.GetProperty("type").GetString() ?? string.Empty, rkey,
                item.GetProperty("created_at").GetInt64() + item.GetProperty("ttl").GetInt64()));
        }

        return keys;
    }

    public async Task<string> GetGroupMemberCardAsync(long groupId, long userId, CancellationToken cancellationToken)
    {
        var data = await CallApiAsync("get_group_member_info", new { group_id = groupId, user_id = userId }, cancellationToken);
        return ReadOptionalString(data, "card");
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
                var message = ReadOptionalString(root, "message");
                throw new InvalidOperationException($"NapCat 接口 {action} 调用失败，retcode={retcode}，message={message}。");
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
                            var groupMessage = GroupMessageParser.ParseGroupMessage(root);
                            if (groupMessage.SenderId == config.BotAccount)
                            {
                                // SQLite 异步接口可能同步执行，将匹配和写入交给线程池，继续接收发送接口响应。
                                _ = Task.Run(() => RecordTextToImageMessageAsync(groupMessage));
                            }

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

    private async Task RecordTextToImageMessageAsync(GroupMessage message)
    {
        try
        {
            await textToImageStore.RecordReceivedAsync(message, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "自身消息的回复图片原文缓存写入失败：群={GroupId}，消息ID={MessageId}",
                message.GroupId, message.MessageId);
        }
    }

    private static bool IsGroupMessageEvent(JsonElement root)
    {
        // message_sent 是机器人自身消息的上报，同样触发记录，再由处理端排除自身消息。
        return root.TryGetProperty("post_type", out var postType)
            && postType.GetString() is "message" or "message_sent"
            && root.GetProperty("message_type").GetString() == "group";
    }

    private static string ReadOptionalString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
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

