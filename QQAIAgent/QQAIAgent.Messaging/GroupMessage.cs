using QQAIAgent.Messaging.Content;

namespace QQAIAgent.Messaging;

public class GroupMessage
{
    #region properties
    public long? MessageId { get; set; }
    public long GroupId { get; set; }
    public long SenderId { get; set; }
    public long? RefMessageId { get; set; }
    public List<MessageContent> MessageContentList { get; set; } = [];
    /// <summary>原始消息是否含有艾特（需在移除机器人艾特前记录）。</summary>
    /// <summary>回复时是否仍需艾特发送者：无法引用消息，或引用的提问消息本身不带艾特时需要。</summary>
    public bool NeedAtInReply => true;
    #endregion
}
