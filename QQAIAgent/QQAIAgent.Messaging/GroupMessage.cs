using QQAIAgent.Messaging.Content;

namespace QQAIAgent.Messaging;

public class GroupMessage
{
    #region properties
    public long? MessageId { get; set; }
    /// <summary>消息发送时间，Unix 秒；未提供时为空。</summary>
    public long? MessageTime { get; set; }
    public long GroupId { get; set; }
    public long SenderId { get; set; }
    public string SenderNickname { get; set; } = "";
    public string SenderCard { get; set; } = "";
    public string SubType { get; set; } = "";
    /// <summary>完整原始正文 JSON，保留处理前的消息段或 CQ 字符串。</summary>
    public string ContentJson { get; set; } = "";
    public long? RefMessageId { get; set; }
    public List<MessageContent> MessageContentList { get; set; } = [];
    /// <summary>回复时是否仍需艾特发送者</summary>
    public bool NeedAtInReply => true;
    #endregion
}
