namespace QQAIAgent.Abstractions.Tool;

/// <summary>同一条用户消息的工具共享此上下文，不同用户消息分别创建。</summary>
public sealed class ToolContext
{
    #region properties
    public long GroupId { get; set; }
    public long SenderId { get; set; }
    /// <summary>本次请求最终回复要艾特的用户，多轮工具调用累积并去重。</summary>
    public HashSet<long> AtUserIds { get; } = [];
    /// <summary>本次请求最终回复附带的图片地址，多轮工具调用按生成顺序累积。</summary>
    public List<string> ReplyImageUrls { get; } = [];
    public ReasoningEffort ReasoningEffort { get; set; } = ReasoningEffort.Minimal;
    #endregion
}
