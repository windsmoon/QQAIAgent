using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using QQAIAgent.Abstractions.Tool;
using QQAIAgent.Tools.Config;

namespace QQAIAgent.Tools;

public sealed class CheckContextRelevanceTool : ITool, IDisposable
{
    #region fields
    private readonly HttpClient _httpClient;
    #endregion

    #region properties
    public string Name => "check_context_relevance";
    #endregion

    #region methods
    public CheckContextRelevanceTool()
    {
        // 超时统一由工具执行器传入的 CancellationToken 控制。
        _httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>创建可直接传给 ExecuteAsync 的问题和候选上下文参数 JSON。</summary>
    public static string CreateRequestJson(string query, List<string> candidateList)
    {
        return JsonSerializer.Serialize(new { query, contexts = candidateList });
    }

    /// <summary>对 Agent 传入的候选上下文进行百炼 rerank 排序，按配置阈值判断与问题的关联性。</summary>
    public async Task<ToolResult> ExecuteAsync(string argumentsJson, ToolContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var arguments = JsonDocument.Parse(argumentsJson);
        var query = arguments.RootElement.GetProperty("query").GetString();
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var candidateList = new List<string>();
        foreach (var candidate in arguments.RootElement.GetProperty("contexts").EnumerateArray())
        {
            var text = candidate.GetString();
            candidateList.Add(text);
        }

        if (candidateList.Count == 0)
        {
            throw new ArgumentException("至少需要一段候选上下文文本。", nameof(argumentsJson));
        }

        // 仅在 Agent 调用工具时读取配置，不自动读取或筛选群历史消息。
        var configPath = Path.Combine(AppContext.BaseDirectory, "Config", "contextrelevance.local.json");
        var configJson = await File.ReadAllTextAsync(configPath, cancellationToken);
        var config = JsonSerializer.Deserialize<ContextRelevanceConfig>(configJson)
            ?? throw new InvalidDataException($"上下文关联配置不能为 null：{configPath}");

        using var request = new HttpRequestMessage(HttpMethod.Post, config.Endpoint)
        {
            Content = JsonContent.Create(new
            {
                model = config.Model,
                input = new { query, documents = candidateList },
                parameters = new { top_n = candidateList.Count, instruct = config.Instruct }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var responseDocument = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        var result = responseDocument.RootElement;
        if (result.TryGetProperty("code", out var errorCode))
        {
            throw new InvalidOperationException($"上下文关联判断失败：{errorCode.GetString()}，{result.GetProperty("message").GetString()}");
        }

        var rankedContexts = result.GetProperty("output").GetProperty("results");
        var results = new List<object>(rankedContexts.GetArrayLength());
        var hasRelatedContext = false;
        foreach (var rankedContext in rankedContexts.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 排序结果的 index 是原始候选下标，不能使用排序后的序号。
            var index = rankedContext.GetProperty("index").GetInt32();
            var relevanceScore = rankedContext.GetProperty("relevance_score").GetSingle();
            var isRelated = relevanceScore >= config.RelevanceThreshold;
            hasRelatedContext = hasRelatedContext || isRelated;
            results.Add(new { index, relevanceScore, isRelated });
        }

        return new ToolResult(JsonSerializer.Serialize(new
        {
            hasRelatedContext,
            relevanceThreshold = config.RelevanceThreshold,
            results
        }));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
    #endregion
}
