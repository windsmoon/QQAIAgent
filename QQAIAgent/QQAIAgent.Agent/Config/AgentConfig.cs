namespace QQAIAgent.Agent.Config;

public class AgentConfig
{
    #region prperties
    /// <summary>允许 Agent 处理消息的 QQ 群号。</summary>
    public List<long> AllowedGroups { get; set; } = [];
    public bool IsTestMode { get; set; }
    /// <summary>每条 QQ 请求允许调用模型的最大次数，包含首次请求和生成最终回复的请求。</summary>
    public int MaxModelCalls { get; set; } = 20;
    /// <summary>允许展开的最大引用层数，不包含当前消息；0 表示不读取引用。</summary>
    public int MaxReferenceDepth { get; set; } = 3;
    /// <summary>用于指导 QQ 群聊回复格式和风格的系统提示词；空白时不发送系统消息。</summary>
    public string SystemPrompt { get; set; } = string.Empty;
    #endregion
    
}
