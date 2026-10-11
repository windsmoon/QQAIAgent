using QQAIAgent.Abstractions.Tools;

namespace QQAIAgent.Tools;

public sealed class GetTimeTool : ITool
{
    #region properties
    public string Name => "get_time";
    #endregion

    #region methods
    public Task<string> ExecuteAsync(string argumentsJson, ToolContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(DateTimeOffset.Now.ToString("O"));
    }
    #endregion
}
