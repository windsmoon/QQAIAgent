using QQAIAgent.Agent.Chat.Content;

namespace QQAIAgent.Agent.Chat;

public class ChatMessage
{
    #region fields
    private static ChatToolCall[] s_emptyToolCalls = Array.Empty<ChatToolCall>();
    #endregion
    
    #region properties
    public required ChatRole Role { get; set; }
    /// <summary>按正文顺序排列的文本和图片内容段。</summary>
    public List<ChatContent> ContentList { get; set; } = [];
    /// <summary>模型返回的思考内容，供需要保留思考上下文的模型类库回传。</summary>
    public string ReasoningContent { get; set; } = string.Empty;
    public IReadOnlyList<ChatToolCall> ToolCalls { get; set; } = s_emptyToolCalls;
    public string ToolCallId { get; set; }
    #endregion
}
