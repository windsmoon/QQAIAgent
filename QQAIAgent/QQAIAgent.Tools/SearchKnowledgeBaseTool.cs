using System.Text.Json;
using QQAIAgent.Abstractions.Tool;

namespace QQAIAgent.Tools;

public sealed class SearchKnowledgeBaseTool : ITool
{
    #region properties
    public string Name => "search_knowledge_base";
    #endregion

    #region methods
    public Task<ToolResult> ExecuteAsync(string argumentsJson, ToolContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var arguments = JsonDocument.Parse(argumentsJson);
        var root = arguments.RootElement;
        var query = root.GetProperty("query").GetString() ?? "";
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var keywords = new List<string>();
        foreach (var item in root.GetProperty("keywords").EnumerateArray())
        {
            var keyword = item.GetString() ?? "";
            ArgumentException.ThrowIfNullOrWhiteSpace(keyword);
            keywords.Add(keyword);
        }
        if (keywords.Count == 0)
        {
            throw new ArgumentException("keywords 至少需要一个关键词。");
        }

        var ftsOperator = root.GetProperty("fts_operator").GetString() ?? "";
        if (ftsOperator is not ("and" or "or"))
        {
            throw new ArgumentException("fts_operator 只能是 and 或 or。");
        }

        var limit = root.TryGetProperty("limit", out var limitValue) ? limitValue.GetInt32() : 20;
        if (limit is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "limit 必须为 1 到 100。");
        }

        // TODO：在 context.GroupId 对应的知识库中，将 keywords 和 ftsOperator 传给 FTS SearchAsync。
        // TODO：用 query 生成查询向量并进行语义检索，再合并、去重、排序，最多返回 limit 条。
        // 框架阶段明确返回未实现，避免将尚未执行检索误报为没有相关记录。
        return Task.FromResult(new ToolResult(JsonSerializer.Serialize(new
        {
            status = "not_implemented",
            message = "知识库混合检索尚未实现，本次未执行 FTS 或向量检索，不能据此判断是否存在相关资料。",
            groupId = context.GroupId,
            query,
            keywords,
            fts_operator = ftsOperator,
            limit
        })));
    }
    #endregion
}
