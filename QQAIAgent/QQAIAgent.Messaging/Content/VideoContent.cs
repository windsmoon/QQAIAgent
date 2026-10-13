namespace QQAIAgent.Messaging.Content;

public class VideoContent : MessageContent
{
    #region properties
    public override MessageContentType ContentType => MessageContentType.Video;
    /// <summary>视频文件标识或位置，保留消息来源提供的值。</summary>
    public string File { get; set; } = "";
    /// <summary>以群文件形式发送的视频资源 ID，用于查询下载地址。</summary>
    public string FileId { get; set; } = "";
    /// <summary>来源群号，在展开合并转发后仍用于定位群文件。</summary>
    public long SourceGroupId { get; set; }
    public string Url { get; set; } = "";
    /// <summary>视频文件大小，单位为字节；未提供时为 0。</summary>
    public long FileSize { get; set; }
    #endregion
}
