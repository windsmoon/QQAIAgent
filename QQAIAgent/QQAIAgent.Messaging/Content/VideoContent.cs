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
    /// <summary>视频所在原消息的 ID；展开引用或合并转发时保留，缺失时不缓存解析结果。</summary>
    public long? SourceMessageId { get; set; }
    /// <summary>原消息内从 0 开始的视频索引，只统计视频内容，展开转发后不重新编号。</summary>
    public int VideoIndex { get; set; }
    public string Url { get; set; } = "";
    /// <summary>视频文件大小，单位为字节；未提供时为 0。</summary>
    public long FileSize { get; set; }
    #endregion
}
