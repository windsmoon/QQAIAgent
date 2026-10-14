namespace QQAIAgent.Agent.Chat;

public class ChatToolDefinition
{
    #region properties
    public required string Name { get; set; }
    public string Description { get; set; } = "";
    public string ParametersJson { get; set; } = "";
    /// <summary>是否允许模型调用工具，未配置时默认启用；Agent 的固定前置调用不受此设置限制。</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>每次调用该工具的超时秒数，未配置时默认为 30 秒。</summary>
    public int TimeoutSeconds { get; set; } = 30;
    #endregion
}
