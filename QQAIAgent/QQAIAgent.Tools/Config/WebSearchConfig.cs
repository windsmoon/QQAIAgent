namespace QQAIAgent.Tools.Config;

public sealed class WebSearchConfig
{
    #region properties
    public string ApiKey { get; set; } = string.Empty;
    /// <summary>联网搜索 API 的完整请求地址。</summary>
    public string Endpoint { get; set; } = string.Empty;
    #endregion
}
