namespace QQAIAgent.NapCat.Config;

public class NapCatConfig
{
    #region properties
    /// <summary>The URL of the NapCat forward WebSocket server.</summary>
    public string WebSocketUrl { get; set; } = "";
    /// <summary>The OneBot WebSocket access token, matching the token configured in NapCat.</summary>
    public string AccessToken { get; set; } = "";
    /// <summary>The expected bot QQ account ID, verified after connecting.</summary>
    public long BotAccount { get; set; }
    /// <summary>启动连接与账号核验共用的总超时秒数。</summary>
    public int StartupTimeoutSeconds { get; set; } = 30;
    /// <summary>单次 NapCat 接口调用的超时秒数，包含等待接口锁、发送请求和接收响应。</summary>
    public int ApiTimeoutSeconds { get; set; } = 30;
    /// <summary>断开连接的超时秒数，包含等待接口锁、关闭握手和等待接收循环结束。</summary>
    public int DisconnectTimeoutSeconds { get; set; } = 5;
    #endregion
}
