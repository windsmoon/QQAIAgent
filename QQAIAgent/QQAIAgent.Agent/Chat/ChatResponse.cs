namespace QQAIAgent.Agent.Chat;

public class ChatResponse
{
    #region properties
    public required ChatMessage Message { get; set; }
    public ChatFinishReason FinishReason { get; set; } = ChatFinishReason.Unknown;
    // 未返回的用量保留 null，区分未知用量与实际为零。
    public long? PromptTokens { get; set; }
    public long? CompletionTokens { get; set; }
    public long? TotalTokens { get; set; }
    // 缓存命中和未命中均属于输入 Token，思考 Token 属于输出 Token，不重复计入总量。
    public long? PromptCacheHitTokens { get; set; }
    public long? PromptCacheMissTokens { get; set; }
    public long? ReasoningTokens { get; set; }
    #endregion
}
