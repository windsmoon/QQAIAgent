using System.Net.WebSockets;
using System.Text.Json;
using QQAIAgent.NapCat.Config;

namespace QQAIAgent.NapCat;

public class NapCatClient(NapCatConfig config) : IDisposable
{
    #region fields
    private readonly ClientWebSocket _socket = new();
    private readonly byte[] _receiveBuffer = new byte[8192];
    #endregion

    #region methods
    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(config.AccessToken) == false)
        {
            _socket.Options.SetRequestHeader("Authorization", $"Bearer {config.AccessToken}");
        }

        return _socket.ConnectAsync(new Uri(config.WebSocketUrl), cancellationToken);
    }

    public async Task VerifyAccountAsync(CancellationToken cancellationToken)
    {
        const string echo = "get_login_info";
        var request = JsonSerializer.SerializeToUtf8Bytes(new
        {
            action = "get_login_info",
            echo
        });

        await _socket.SendAsync(request.AsMemory(), WebSocketMessageType.Text, true, cancellationToken);

        while (true)
        {
            using var response = await ReceiveMessageAsync(cancellationToken)
                ?? throw new IOException("NapCat 在账号核验完成前关闭了连接。");
            var root = response.RootElement;

            // 同一连接会交错收到 API 响应和事件，只处理本次登录信息请求的响应。
            if (root.TryGetProperty("echo", out var responseEcho) == false
                || responseEcho.ValueKind != JsonValueKind.String
                || responseEcho.GetString() != echo)
            {
                continue;
            }

            var retcode = root.GetProperty("retcode").GetInt32();
            if (root.GetProperty("status").GetString() != "ok" || retcode != 0)
            {
                throw new InvalidOperationException($"NapCat 获取登录信息失败，retcode={retcode}。");
            }

            var account = root.GetProperty("data").GetProperty("user_id").GetInt64();
            if (account != config.BotAccount)
            {
                throw new InvalidOperationException("NapCat 登录账号与配置的机器人账号不一致。");
            }

            return;
        }
    }

    public async Task WaitForDisconnectAsync(CancellationToken cancellationToken)
    {
        while (cancellationToken.IsCancellationRequested == false)
        {
            // 本阶段只保持接收以发现连接关闭，后续在这里接入消息事件处理。
            using var message = await ReceiveMessageAsync(cancellationToken);
            if (message is null)
            {
                return;
            }
        }
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

            // WebSocket 消息可能分成多帧，必须收齐后再解析 JSON。
            message.Write(_receiveBuffer, 0, result.Count);
        }
        while (result.EndOfMessage == false);

        message.Position = 0;
        return await JsonDocument.ParseAsync(message, cancellationToken: cancellationToken);
    }

    public async Task DisconnectAsync()
    {
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            // 停止令牌此时可能已取消，关闭握手单独限时，避免退出一直等待远端。
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Stopping", timeout.Token);
            }
            catch (OperationCanceledException)
            {
                _socket.Abort();
            }
            catch (WebSocketException)
            {
                _socket.Abort();
            }
        }
    }

    public void Dispose()
    {
        _socket.Dispose();
    }
    #endregion
}

