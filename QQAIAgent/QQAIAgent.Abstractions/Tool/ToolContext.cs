namespace QQAIAgent.Abstractions.Tool;

/// <summary>同一条用户消息的工具共享此上下文，不同用户消息分别创建。</summary>
public sealed class ToolContext
{
    #region properties
    public long GroupId { get; set; }
    public ReasoningEffort ReasoningEffort { get; set; } = ReasoningEffort.Minimal;
    #endregion
}
