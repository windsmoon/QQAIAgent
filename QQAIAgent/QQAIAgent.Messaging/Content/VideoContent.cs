namespace QQAIAgent.Messaging.Content;

public class VideoContent : MessageContent
{
    #region properties
    public override MessageContentType ContentType => MessageContentType.Video;
    /// <summary>视频文件标识或位置，保留消息来源提供的值。</summary>
    public string File { get; set; } = "";
    public string Url { get; set; } = "";
    /// <summary>视频文件大小，单位为字节；未提供时为 0。</summary>
    public long FileSize { get; set; }
    #endregion
}
