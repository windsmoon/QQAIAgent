namespace QQAIAgent.Agent.Chat;

public class ChatToolCall
{
    #region properties
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string ArgumentsJson { get; set; } = "";
    #endregion
}
