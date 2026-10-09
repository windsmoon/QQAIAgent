namespace QQAIAgent.NapCat.Config;

public class NapCatConfig
{
    /// <summary>NapCat 正向 WebSocket 服务地址。</summary>
    public string WebSocketUrl { get; set; } = "";
    /// <summary>OneBot WebSocket 服务的访问令牌，与 NapCat 中的 token 一致。</summary>
    public string AccessToken { get; set; } = "";
    /// <summary>预期登录的机器人 QQ 号，用于连接后核验账号。</summary>
    public long BotAccount { get; set; }
}
