namespace QQAIAgent.Messaging.Content;

/// <summary>尚未支持的消息段，保留原始类型和数据供后续处理。</summary>
public class UnknownContent : MessageContent
{
    #region properties
    public override MessageContentType ContentType => MessageContentType.Unknown;
    public string OriginalType { get; set; } = "";
    /// <summary>消息段 data 的 JSON 文本，不依赖接收端 JsonDocument 的生命周期。</summary>
    public string DataJson { get; set; } = "";
    #endregion
}
