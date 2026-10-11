using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using QQAIAgent.Abstractions.Tool;
using QQAIAgent.Tools.Config;

namespace QQAIAgent.Tools;

public sealed class WebSearchTool : ITool, IDisposable
{
    #region fields
    private readonly HttpClient _httpClient;
    #endregion

    #region properties
    public string Name => "web_search";
    #endregion

    #region methods
    public WebSearchTool()
    {
        // 超时统一由工具执行器传入的 CancellationToken 控制。
        _httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>通过腾讯云 SearchPro 搜索，返回查询词、搜索结果、请求 ID 和服务提示。</summary>
    public async Task<string> ExecuteAsync(string argumentsJson, ToolContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var arguments = JsonDocument.Parse(argumentsJson);
        var query = arguments.RootElement.GetProperty("query").GetString();
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        // 在实际调用时读取配置，未使用联网搜索时不依赖此配置文件。
        var configPath = Path.Combine(AppContext.BaseDirectory, "Config", "websearch.local.json");
        var configJson = await File.ReadAllTextAsync(configPath, cancellationToken);
        var config = JsonSerializer.Deserialize<WebSearchConfig>(configJson)
            ?? throw new InvalidDataException($"联网搜索配置不能为 null：{configPath}");

        using var request = new HttpRequestMessage(HttpMethod.Post, config.Endpoint)
        {
            // 字典保留腾讯云要求的 Query 大小写，避免默认 JSON 命名策略改成 query。
            Content = JsonContent.Create(new Dictionary<string, string> { ["Query"] = query })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var responseDocument = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        var searchResponse = responseDocument.RootElement.GetProperty("Response");
        // 腾讯云可能在 HTTP 成功响应中返回业务错误，交给现有工具执行器处理。
        if (searchResponse.TryGetProperty("Error", out var error))
        {
            throw new InvalidOperationException($"腾讯云联网搜索失败：{error.GetProperty("Code").GetString()}，{error.GetProperty("Message").GetString()}");
        }

        var pages = searchResponse.GetProperty("Pages");
        var results = new List<JsonElement>(pages.GetArrayLength());
        foreach (var page in pages.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Pages 的每一项是 JSON 字符串；解码为对象，保留各服务版本返回的结果字段。
            results.Add(JsonSerializer.Deserialize<JsonElement>(page.GetString()!));
        }

        return JsonSerializer.Serialize(new
        {
            query,
            results,
            requestId = searchResponse.GetProperty("RequestId").GetString(),
            message = searchResponse.TryGetProperty("Msg", out var message) ? message.GetString() : null
        });
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
    #endregion
}
