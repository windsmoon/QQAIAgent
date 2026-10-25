using System.Text.Json;
using QQAIAgent.Abstractions.Messages;
using QQAIAgent.Abstractions.Tool;

namespace QQAIAgent.Tools;

public sealed class SearchKnowledgeBaseTool(IGroupMessageQuery messageQuery) : ITool
{
    #region fields
    private static readonly JsonSerializerOptions ResultOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    #endregion

    #region properties
    public string Name => "search_knowledge_base";
    #endregion

    #region methods
    public async Task<ToolResult> ExecuteAsync(string argumentsJson, ToolContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var arguments = JsonDocument.Parse(argumentsJson);
        var root = arguments.RootElement;
        var query = "";
        if (root.TryGetProperty("query", out var queryValue))
        {
            query = queryValue.GetString() ?? "";
            ArgumentException.ThrowIfNullOrWhiteSpace(query);
        }

        var keywords = new List<string>();
        if (root.TryGetProperty("keywords", out var keywordsValue))
        {
            foreach (var item in keywordsValue.EnumerateArray())
            {
                var keyword = item.GetString() ?? "";
                ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
                keywords.Add(keyword);
            }
            if (keywords.Count == 0)
            {
                throw new ArgumentException("keywords 至少需要一个关键词。");
            }
        }
        if (keywords.Count == 0 && string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("keywords 和 query 至少提供一项。");
        }

        var ftsOperator = root.TryGetProperty("fts_operator", out var operatorValue) ? operatorValue.GetString() : "or";
        if (ftsOperator is not ("and" or "or"))
        {
            throw new ArgumentException("fts_operator 只能是 and 或 or。");
        }

        var limit = root.TryGetProperty("limit", out var limitValue) ? limitValue.GetInt32() : 20;
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "limit 必须为 1 到 100。");
        }

        var messages = await messageQuery.SearchKnowledgeAsync(context.GroupId, keywords, ftsOperator, query, limit, cancellationToken);
        return new ToolResult(JsonSerializer.Serialize(new
        {
            groupId = context.GroupId,
            query,
            keywords,
            fts_operator = ftsOperator,
            limit,
            returnedCount = messages.Count,
            messages
        }, ResultOptions));
    }
    #endregion
}
