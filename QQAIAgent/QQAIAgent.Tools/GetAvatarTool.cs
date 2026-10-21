using System.Text.Json;
using QQAIAgent.Abstractions.Tool;
using QQAIAgent.Messaging;

namespace QQAIAgent.Tools;

public sealed class GetAvatarTool(IMessageReader messageReader) : ITool
{
    #region properties
    public string Name => "get_avatar";
    #endregion

    #region methods
    public async Task<ToolResult> ExecuteAsync(string argumentsJson, ToolContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var arguments = JsonDocument.Parse(argumentsJson);
        var userId = arguments.RootElement.GetProperty("user_id").GetInt64();
        if (userId <= 0)
        {
            throw new ArgumentException("user_id 必须为正整数 QQ 号。", nameof(argumentsJson));
        }

        var url = await messageReader.GetUserAvatarUrlAsync(userId, cancellationToken);
        return new ToolResult(JsonSerializer.Serialize(string.Empty)) { ImageUrls = [url] };
    }
    #endregion
}
