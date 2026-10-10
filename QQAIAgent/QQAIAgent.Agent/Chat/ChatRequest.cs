namespace QQAIAgent.Agent.Chat;

public class ChatRequest
{
    #region properties
    public required IReadOnlyList<ChatMessage> Messages { get; set; }
    public IReadOnlyList<ChatToolDefinition> Tools { get; } = [];
    public int? MaxOutputTokens { get; set; }
    #endregion
}
