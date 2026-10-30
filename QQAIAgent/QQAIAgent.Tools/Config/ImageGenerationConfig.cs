namespace QQAIAgent.Tools.Config;

public sealed class ImageGenerationConfig
{
    #region properties
    public string ApiKey { get; set; } = string.Empty;
    /// <summary>百炼 OpenAI 兼容接口的完整 images/generations 请求地址，与 ApiKey 属于同一地域。</summary>
    public string Endpoint { get; set; } = "https://dashscope.aliyuncs.com/compatible-mode/v1/images/generations";
    #endregion
}
