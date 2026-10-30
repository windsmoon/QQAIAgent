namespace QQAIAgent.Messaging.Content;

public class ImageContent : MessageContent
{
    #region properties
    public override MessageContentType ContentType => MessageContentType.Image;
    /// <summary>图片文件标识或位置，保留消息来源提供的值。</summary>
    public string File { get; set; } = "";
    /// <summary>解析时按群号、消息号、原消息全部内容中从 0 开始的索引生成；展开引用或转发时保留，缺少来源标识时为空。</summary>
    public string CacheKey { get; set; } = "";
    public long SourceGroupId { get; set; }
    public long? SourceMessageId { get; set; }
    /// <summary>原消息中从 0 开始的图片序号，包含商城表情，不计文本和普通 QQ 表情；展开或过滤消息后保持不变。</summary>
    public int ImageIndex { get; set; }
    public string Url { get; set; } = "";
    public string Summary { get; set; } = "";
    /// <summary>图片子类型，保留消息来源提供的值。</summary>
    public ImageSubType SubType { get; set; }
    /// <summary>图片文件大小，单位为字节；未提供时为 0。</summary>
    public long FileSize { get; set; }
    #endregion
}
