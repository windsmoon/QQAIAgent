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

            // API responses and events share this connection; only handle the response to this login info request.
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
            // For now, keep receiving to detect disconnection; message event handling will be added here later.
            using var message = await ReceiveMessageAsync(cancellationToken);
            if (message is null)
            {
                return;
            }
        }
    }
    
    public async Task DisconnectAsync()
    {
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            // The stopping token may already be canceled; use a separate timeout for the closing handshake.
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

