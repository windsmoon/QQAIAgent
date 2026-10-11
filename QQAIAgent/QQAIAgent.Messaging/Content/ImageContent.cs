namespace QQAIAgent.Messaging.Content;

public class ImageContent : MessageContent
{
    #region properties
    public override MessageContentType ContentType => MessageContentType.Image;
    /// <summary>图片文件标识或位置，保留消息来源提供的值。</summary>
    public string File { get; set; } = "";
    public string Url { get; set; } = "";
    public string Summary { get; set; } = "";
    /// <summary>图片子类型，保留消息来源提供的值。</summary>
    public int SubType { get; set; }
    /// <summary>图片文件大小，单位为字节；未提供时为 0。</summary>
    public long FileSize { get; set; }
    #endregion
}
