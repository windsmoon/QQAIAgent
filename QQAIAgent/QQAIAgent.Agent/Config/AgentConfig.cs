namespace QQAIAgent.Agent.Config;

public class AgentConfig
{
    #region prperties
    /// <summary>允许 Agent 处理消息的 QQ 群号。</summary>
    public List<long> AllowedGroups { get; set; } = [];
    /// <summary>每条 QQ 请求允许调用模型的最大次数，包含首次请求和生成最终回复的请求。</summary>
    public int MaxModelCalls { get; set; } = 20;
    #endregion
    
}
