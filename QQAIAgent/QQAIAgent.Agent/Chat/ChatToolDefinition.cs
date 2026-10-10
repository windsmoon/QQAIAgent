namespace QQAIAgent.Agent.Chat;

public class ChatToolDefinition
{
    #region properties
    public required string Name { get; set; }
    public string Description { get; set; } = "";
    public string ParametersJson { get; set; } = "";
    #endregion
}
