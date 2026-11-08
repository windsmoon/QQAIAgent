using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using QQAIAgent.Abstractions.Tool;
using QQAIAgent.Infrastructure.Runtime;

namespace QQAIAgent.Tools;

public sealed class DsWebFetchTool : ITool, IDisposable
{
    #region fields
    private readonly HttpClient _httpClient;
    #endregion

    #region properties
    public string Name => "ds_web_fetch";
    #endregion

    #region methods
    public DsWebFetchTool()
    {
        // 超时统一由工具执行器传入的 CancellationToken 控制。
        _httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>通过 DeepSeek Fetch API 获取网页，返回请求地址、标题和 Markdown 正文。</summary>
    public async Task<ToolResult> ExecuteAsync(string argumentsJson, ToolContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var arguments = JsonDocument.Parse(argumentsJson);
        var url = arguments.RootElement.GetProperty("url").GetString();
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        if (url.Length > 8192 || Uri.TryCreate(url, UriKind.Absolute, out var uri) == false ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(uri.Host) || string.IsNullOrEmpty(uri.UserInfo) == false)
        {
            throw new ArgumentException("url 必须是不含用户名和密码的 HTTP 或 HTTPS 绝对地址，且不超过 8192 字符。", nameof(argumentsJson));
        }

        // 复用 DeepSeek 密钥，仅在调用时读取所需字段，避免工具插件依赖模型适配程序集。
        var configPath = Path.Combine(ApplicationPaths.RootDirectory, "Config", "deepseek.local.json");
        var configJson = await File.ReadAllTextAsync(configPath, cancellationToken);
        using var config = JsonDocument.Parse(configJson);
        var apiKey = config.RootElement.GetProperty("ApiKey").GetString();
        var endpoint = config.RootElement.GetProperty("FetchEndpoint").GetString();

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new { url })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.IsSuccessStatusCode == false)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException($"DeepSeek 网页获取失败：HTTP={(int)response.StatusCode}，响应={errorBody[..Math.Min(errorBody.Length, 2048)]}",
                null, response.StatusCode);
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var responseDocument = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        var page = responseDocument.RootElement;
        return new ToolResult(JsonSerializer.Serialize(new
        {
            url,
            title = page.GetProperty("title").GetString(),
            content = page.GetProperty("content").GetString()
        }));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
    #endregion
}
