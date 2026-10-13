using System.Text.Json.Serialization;

namespace QQAIAgent.Agent.Config;

public class AgentConfig
{
    #region prperties
    /// <summary>允许 Agent 处理消息的 QQ 群号。</summary>
    public List<long> AllowedGroups { get; set; } = [];
    /// <summary>黑名单群号只记录消息，不处理</summary>
    public List<long> BlacklistedGroups { get; set; } = [];
    public bool IsTestMode { get; set; }
    /// <summary>每条 QQ 请求允许调用模型的最大次数，包含首次请求和生成最终回复的请求。</summary>
    public int MaxModelCalls { get; set; } = 20;
    /// <summary>每条 QQ 用户请求从接收到正常回复的总超时秒数，包含排队、引用读取、所有模型调用和工具执行。</summary>
    public int QQRequestTimeoutSeconds { get; set; } = 300;
    /// <summary>允许展开的最大引用层数，不包含当前消息；0 表示不读取引用。</summary>
    public int MaxReferenceDepth { get; set; } = 3;
    /// <summary>启动时从 Markdown 文件加载的系统提示词；空白时不发送系统消息。</summary>
    [JsonIgnore]
    public string SystemPrompt { get; set; } = string.Empty;
    #endregion
    
}
