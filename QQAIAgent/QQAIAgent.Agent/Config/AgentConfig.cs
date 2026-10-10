namespace QQAIAgent.Agent.Config;

public class AgentConfig
{
    #region prperties
    /// <summary>允许 Agent 处理消息的 QQ 群号。</summary>
    public List<long> AllowedGroups { get; set; } = [];
    #endregion
    
}
