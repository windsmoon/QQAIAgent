namespace QQAIAgent.Messaging;

public class GroupMessage
{
    #region properties
    public long GroupId { get; set; }
    public long SenderId { get; set; }
    public string Text { get; set; } = "";
    #endregion
}
