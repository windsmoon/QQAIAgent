using System.Text.Json;
using QQAIAgent.Abstractions;
using QQAIAgent.Abstractions.Tool;

namespace QQAIAgent.Tools;

public sealed class SetReasoningEffortTool : ITool
{
    #region properties
    public string Name => "set_reasoning_effort";
    #endregion

    #region methods
    public Task<string> ExecuteAsync(string argumentsJson, ToolContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var arguments = JsonDocument.Parse(argumentsJson);
        context.ReasoningEffort = arguments.RootElement.GetProperty("effort").GetString() switch
        {
            "minimal" => ReasoningEffort.Minimal,
            "low" => ReasoningEffort.Low,
            "medium" => ReasoningEffort.Medium,
            "high" => ReasoningEffort.High,
            "max" => ReasoningEffort.Max,
            _ => throw new ArgumentException("effort 必须为 minimal、low、medium、high 或 max。", nameof(argumentsJson))
        };
        return Task.FromResult($"下一次模型请求将使用 {context.ReasoningEffort} 思考强度。");
    }
    #endregion
}
