namespace QQAIAgent.Abstractions.Messages;

public sealed class GroupMessageSearchItem
{
    public long RecordId { get; set; }
    public long? MessageId { get; set; }
    public long MessageTime { get; set; }
    public long SenderId { get; set; }
    public string SenderNickname { get; set; } = "";
    public string SenderCard { get; set; } = "";
    public long? RefMessageId { get; set; }
    public string Text { get; set; } = "";
    public bool TextTruncated { get; set; }
}