namespace QQAIAgent.DeepSeek.Config;

public sealed class DeepSeekConfig
{
    #region properties
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    /// <summary>ds_web_fetch 使用的网页获取 API 完整地址，与对话接口共用 ApiKey。</summary>
    public string FetchEndpoint { get; set; } = "https://api.deepseek.com/v1/tools/fetch";
    public int TimeoutSeconds { get; set; } = 60;
    #endregion
}
