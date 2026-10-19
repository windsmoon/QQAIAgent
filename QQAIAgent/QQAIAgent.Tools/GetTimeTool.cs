using QQAIAgent.Abstractions.Tool;

namespace QQAIAgent.Tools;

public sealed class GetTimeTool : ITool
{
    #region properties
    public string Name => "get_time";
    #endregion

    #region methods
    public Task<ToolResult> ExecuteAsync(string argumentsJson, ToolContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ToolResult(DateTimeOffset.Now.ToString("O")));
    }
    #endregion
}
