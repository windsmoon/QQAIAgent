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
    public int MaxOutputTokens { get; set; } = 4096;
    /// <summary>允许输入的视频大小上限，单位为 MB，按 1024 × 1024 字节计算。</summary>
    public int MaxVideoSizeMB { get; set; } = 100;
    /// <summary>允许输入的视频时长上限，单位为秒。</summary>
    public double MaxVideoDurationSeconds { get; set; } = 180;
    /// <summary>视频解析缓存有效期，单位为秒，默认 30 天；为 null 时忽略记录日期。</summary>
    public long? CacheDurationSeconds { get; set; } = 30L * 24 * 60 * 60;
    /// <summary>ffprobe 可执行文件；默认使用随程序部署的版本，带目录的相对路径以程序目录为基准。</summary>
    public string FfprobePath { get; set; } = "Third/FFmpeg/ffprobe.exe";
    #endregion
}
