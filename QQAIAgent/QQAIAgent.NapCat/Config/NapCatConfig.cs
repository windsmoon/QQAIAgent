namespace QQAIAgent.NapCat.Config;

public class NapCatConfig
{
    /// <summary>The URL of the NapCat forward WebSocket server.</summary>
    public string WebSocketUrl { get; set; } = "";
    /// <summary>The OneBot WebSocket access token, matching the token configured in NapCat.</summary>
    public string AccessToken { get; set; } = "";
    /// <summary>The expected bot QQ account ID, verified after connecting.</summary>
    public long BotAccount { get; set; }
}
