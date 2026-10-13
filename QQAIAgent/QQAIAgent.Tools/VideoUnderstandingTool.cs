using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using QQAIAgent.Abstractions.Tool;
using QQAIAgent.Tools.Config;

namespace QQAIAgent.Tools;

public sealed class VideoUnderstandingTool : ITool, IDisposable
{
    #region fields
    private readonly HttpClient _httpClient;
    #endregion

    #region properties
    public string Name => "understand_video";
    #endregion

    #region methods
    public VideoUnderstandingTool()
    {
        // 超时统一由工具执行器传入的 CancellationToken 控制。
        _httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>通过阿里云百炼理解视频画面和声音，返回分析内容、模型名称和 Token 用量。</summary>
    public async Task<string> ExecuteAsync(string argumentsJson, ToolContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var arguments = JsonDocument.Parse(argumentsJson);
        var url = arguments.RootElement.GetProperty("url").GetString();
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) == false ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("url 必须是 HTTP 或 HTTPS 视频地址。", nameof(argumentsJson));
        }

        var prompt = arguments.RootElement.GetProperty("prompt").GetString();
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        // 在实际调用时读取配置，未使用视频理解时不依赖此配置文件。
        var configPath = Path.Combine(AppContext.BaseDirectory, "Config", "videounderstanding.local.json");
        var configJson = await File.ReadAllTextAsync(configPath, cancellationToken);
        var config = JsonSerializer.Deserialize<VideoUnderstandingConfig>(configJson)
            ?? throw new InvalidDataException($"视频理解配置不能为 null：{configPath}");

        using var request = new HttpRequestMessage(HttpMethod.Post, config.Endpoint)
        {
            Content = JsonContent.Create(new
            {
                model = config.Model,
                stream = false,
                modalities = new[] { "text" },
                reasoning_effort = config.ReasoningEffort,
                max_tokens = config.MaxTokens,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        // 视频由百炼直接下载并抽帧，客户端只传 URL，不加载整个视频到内存。
                        content = new object[]
                        {
                            new { type = "video_url", video_url = new { url }, fps = config.Fps },
                            new { type = "text", text = prompt }
                        }
                    }
                }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var responseDocument = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        var result = responseDocument.RootElement;
        var choice = result.GetProperty("choices")[0];

        return JsonSerializer.Serialize(new
        {
            url,
            prompt,
            content = choice.GetProperty("message").GetProperty("content").GetString(),
            model = result.GetProperty("model").GetString(),
            requestId = result.GetProperty("id").GetString(),
            finishReason = choice.GetProperty("finish_reason").GetString(),
            usage = result.GetProperty("usage")
        });
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
    #endregion
}
