namespace QQAIAgent.Agent.Chat;

public class ChatResponse
{
    #region properties
    public required ChatMessage Message { get; set; }
    public ChatFinishReason FinishReason { get; set; } = ChatFinishReason.Unknown;
    #endregion
}
