namespace QQAIAgent.Tools.Config;

public sealed class ContextRelevanceConfig
{
    #region properties
    public string ApiKey { get; set; } = string.Empty;
    /// <summary>百炼 DashScope 文本排序接口的完整请求地址。</summary>
    public string Endpoint { get; set; } = string.Empty;
    public string Model { get; set; } = "qwen3-rerank";
    /// <summary>关联分数达到此阈值时视为相关，需按实际对话效果调整。</summary>
    public double RelevanceThreshold { get; set; } = 0.5;
    /// <summary>排序任务说明，用于判断对话上下文关联性。</summary>
    public string Instruct { get; set; } = "Given a user's question in a group conversation, retrieve nearby messages that provide relevant conversational context, including the topic, entities, references, or intent needed to understand the question.";
    #endregion
}
