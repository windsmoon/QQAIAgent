namespace QQAIAgent.Agent.Config;

public class AgentConfig
{
    #region prperties
    /// <summary>允许 Agent 处理消息的 QQ 群号。</summary>
    public List<long> AllowedGroups { get; set; } = [];

    /// <summary>模型 DLL 路径，支持绝对路径或相对于宿主程序目录的路径。</summary>
    public string ChatModelAssembly { get; set; } = "";

    /// <summary>模型实现类型的完整名称，包含命名空间。</summary>
    public string ChatModelType { get; set; } = "";
    #endregion
    
}
