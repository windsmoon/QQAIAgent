using QQAIAgent.Messaging.Content;

namespace QQAIAgent.Messaging;

public class GroupMessage
{
    #region properties
    public long GroupId { get; set; }
    public long SenderId { get; set; }
    public string Text { get; set; } = "";
    public long? RefMessageId { get; set; }
    public List<MessageContent> MessageContentList { get; set; } = [];
    #endregion
}
