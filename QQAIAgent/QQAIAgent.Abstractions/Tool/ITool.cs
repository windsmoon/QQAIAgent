namespace QQAIAgent.Abstractions.Tool;

public interface ITool
{
    #region properties
    /// <summary>工具的唯一名称，与工具配置中的 Name 对应。</summary>
    string Name { get; }
    #endregion

    #region methods
    /// <summary>接收调用参数 JSON 和本条用户消息的上下文，返回可作为工具结果消息内容的文本或 JSON。</summary>
    Task<string> ExecuteAsync(string argumentsJson, ToolContext context, CancellationToken cancellationToken);
    #endregion
}
