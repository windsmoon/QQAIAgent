namespace QQAIAgent.Agent.Chat;

public class ChatMessage
{
    #region fields
    private static ChatToolCall[] s_emptyToolCalls = Array.Empty<ChatToolCall>();
    #endregion
    
    #region properties
    public required ChatRole Role { get; set; }
    public string Content { get; set; }
    public IReadOnlyList<ChatToolCall> ToolCalls { get; set; } = s_emptyToolCalls;
    public string ToolCallId { get; set; }
    #endregion
}
