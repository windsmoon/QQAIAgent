using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using QQAIAgent.Messaging;
using QQAIAgent.NapCat.Config;

namespace QQAIAgent.NapCat;

public class NapCatClient(NapCatConfig config) : IMessageSender, IDisposable
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
    /// Publishes plain text group messages. Handlers run in the receive loop and must not block while waiting for API responses.
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

    public async Task<long> SendGroupMessageAsync(long groupId, string text, CancellationToken cancellationToken)
    {
        var data = await CallApiAsync("send_group_msg", new
        {
            group_id = groupId,
            message = text,
            auto_escape = true
        }, cancellationToken);
        return data.GetProperty("message_id").GetInt64();
    }

    public async Task WaitForDisconnectAsync(CancellationToken cancellationToken)
    {
        var receiveTask = _receiveTask ?? throw new InvalidOperationException("NapCat 尚未建立连接。");
        await receiveTask.WaitAsync(cancellationToken);
    }

    public async Task DisconnectAsync()
    {
        // The stopping token may already be canceled; use a separate timeout for the closing handshake.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
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
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

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
            throw new TimeoutException($"NapCat 接口 {action} 调用超时（30 秒）。");
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
                using var message = await ReceiveMessageAsync(cancellationToken);
                if (message is null)
                {
                    return;
                }

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

                var groupMessage = ParseGroupMessage(root);
                if (groupMessage is not null)
                {
                    GroupMessageReceived?.Invoke(groupMessage);
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

    private static GroupMessage? ParseGroupMessage(JsonElement root)
    {
        if (root.TryGetProperty("post_type", out var postType) == false
            || postType.GetString() != "message"
            || root.GetProperty("message_type").GetString() != "group")
        {
            return null;
        }

        var message = root.GetProperty("message");
        string text;
        // The shared model currently represents plain text only; do not turn mixed messages into text commands.
        if (message.ValueKind == JsonValueKind.String)
        {
            text = message.GetString() ?? "";
            if (text.Contains("[CQ:", StringComparison.Ordinal))
            {
                return null;
            }

            // Decode brackets before ampersands to avoid decoding escaped entity text twice.
            text = text.Replace("&#91;", "[").Replace("&#93;", "]").Replace("&amp;", "&");
        }
        else if (message.ValueKind == JsonValueKind.Array)
        {
            var builder = new StringBuilder();
            foreach (var segment in message.EnumerateArray())
            {
                if (segment.GetProperty("type").GetString() != "text")
                {
                    return null;
                }

                builder.Append(segment.GetProperty("data").GetProperty("text").GetString());
            }

            text = builder.ToString();
        }
        else
        {
            throw new InvalidDataException("NapCat 群消息内容必须是字符串或消息段数组。");
        }

        return new GroupMessage
        {
            GroupId = root.GetProperty("group_id").GetInt64(),
            SenderId = root.GetProperty("user_id").GetInt64(),
            Text = text
        };
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

