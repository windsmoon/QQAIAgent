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
    /// <summary>回复时是否仍需艾特发送者</summary>
    public bool NeedAtInReply => true;
    #endregion
}
