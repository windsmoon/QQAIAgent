namespace QQAIAgent.Agent.Chat;

public class ChatToolDefinition
{
    #region properties
    public required string Name { get; set; }
    public string Description { get; set; } = "";
    public string ParametersJson { get; set; } = "";
    /// <summary>是否启用工具，未配置时默认启用。</summary>
    public bool Enabled { get; set; } = true;
    #endregion
}
