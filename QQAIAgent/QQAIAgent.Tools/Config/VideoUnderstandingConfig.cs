namespace QQAIAgent.Tools.Config;

public sealed class VideoUnderstandingConfig
{
    #region properties
    public string ApiKey { get; set; } = string.Empty;
    /// <summary>百炼 OpenAI 兼容接口的完整 chat/completions 请求地址。</summary>
    public string Endpoint { get; set; } = string.Empty;
    /// <summary>支持直接输入视频文件并理解画面和声音的全模态模型。</summary>
    public string Model { get; set; } = "qwen3.8-omni-flash";
    /// <summary>视频采样帧率，默认每秒 2 帧。</summary>
    public double Fps { get; set; } = 2;
    /// <summary>思考强度，none 关闭思考；支持 minimal、low、medium、high、xhigh、max。</summary>
    public string ReasoningEffort { get; set; } = "none";
    /// <summary>模型单次输出的最大 Token 数。</summary>
    public int MaxTokens { get; set; } = 4096;
    #endregion
}
