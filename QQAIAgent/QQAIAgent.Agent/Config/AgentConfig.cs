using System.Text.Json.Serialization;
using QQAIAgent.Abstractions;

namespace QQAIAgent.Agent.Config;

public class AgentConfig
{
    #region prperties
    /// <summary>允许 Agent 处理消息的 QQ 群号。</summary>
    public List<long> AllowedGroups { get; set; } = [];
    /// <summary>黑名单群号只记录消息，不处理</summary>
    public List<long> BlacklistedGroups { get; set; } = [];
    public bool IsTestMode { get; set; }
    /// <summary>每条 QQ 用户请求首次模型调用的思考强度，后续可由工具调整。</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ReasoningEffort>))]
    public ReasoningEffort FirstReasoningEffort { get; set; } = ReasoningEffort.Low;
    /// <summary>每条 QQ 请求允许调用模型的最大次数，包含首次请求和生成最终回复的请求。</summary>
    public int MaxModelCalls { get; set; } = 20;
    /// <summary>每条 QQ 用户请求从接收到正常回复的总超时秒数，包含排队、引用读取、所有模型调用和工具执行。</summary>
    public int QQRequestTimeoutSeconds { get; set; } = 300;
    /// <summary>回复文本长度超过此值时转为图片发送，否则发送文字。</summary>
    public int TextToImageThreshold { get; set; } = 200;
    /// <summary>允许展开的最大引用层数，不包含当前消息；0 表示不读取引用。</summary>
    public int MaxReferenceDepth { get; set; } = 5;
    /// <summary>合并转发展开的最大嵌套层数；0 表示不展开。</summary>
    public int MaxForwardDepth { get; set; } = 5;
    /// <summary>单层合并转发最多展开的消息条数，超出部分截断。</summary>
    public int MaxForwardMessagesPerLayer { get; set; } = 20;
    /// <summary>关联判断前读取的最近群消息记录数；forward 本身算一条，内部消息段不另计数；保留文字、图片和表情，引用链消息和无这些内容的记录被跳过时仍占用名额，0 表示不读取。</summary>
    public int ContextMessageCount { get; set; } = 15;
    /// <summary>是否输出上下文关联判断的汇总、候选正文和逐条评分日志，默认关闭。</summary>
    public bool IsLogContextRelevanceResults { get; set; }
    /// <summary>启动时从 Markdown 文件加载的系统提示词；空白时不发送系统消息。</summary>
    [JsonIgnore]
    public string SystemPrompt { get; set; } = string.Empty;
    #endregion
    
}
