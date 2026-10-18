namespace QQAIAgent.Messaging.Content;

public class ImageContent : MessageContent
{
    #region properties
    public override MessageContentType ContentType => MessageContentType.Image;
    /// <summary>图片文件标识或位置，保留消息来源提供的值。</summary>
    public string File { get; set; } = "";
    /// <summary>图片所在原消息的群号，展开引用或合并转发时保留。</summary>
    public long SourceGroupId { get; set; }
    /// <summary>图片所在原消息的 ID；缺失时不缓存上传结果。</summary>
    public long? SourceMessageId { get; set; }
    /// <summary>原消息内从 0 开始的图片索引，包含图片表情，展开转发后不重新编号。</summary>
    public int IndexInContents { get; set; }
    public string Url { get; set; } = "";
    public string Summary { get; set; } = "";
    /// <summary>图片子类型，保留消息来源提供的值。</summary>
    public ImageSubType SubType { get; set; }
    /// <summary>图片文件大小，单位为字节；未提供时为 0。</summary>
    public long FileSize { get; set; }
    #endregion
}
