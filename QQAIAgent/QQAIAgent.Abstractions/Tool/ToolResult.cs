namespace QQAIAgent.Abstractions.Tool;

public sealed class ToolResult(string text)
{
    #region properties
    public string Text { get; } = text;
    /// <summary>交给模型读取的图片地址，下载和上传由模型适配层处理。</summary>
    public List<string> ImageUrls { get; set; }
    #endregion
}
