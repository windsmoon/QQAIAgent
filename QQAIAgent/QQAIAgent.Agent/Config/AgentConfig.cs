namespace QQAIAgent.Agent.Config;

public class AgentConfig
{
    #region prperties
    /// <summary>QQ group IDs whose messages the agent is allowed to process.</summary>
    public List<long> AllowedGroups { get; set; } = [];
    #endregion
    
}
