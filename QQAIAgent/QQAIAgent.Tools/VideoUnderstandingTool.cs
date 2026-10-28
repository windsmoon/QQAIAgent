using QQAIAgent.Infrastructure.Runtime;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using QQAIAgent.Abstractions.Tool;
using QQAIAgent.Abstractions.Video;
using QQAIAgent.Tools.Config;

namespace QQAIAgent.Tools;

public sealed class VideoUnderstandingTool : ITool, IDisposable
{
    #region fields
    private readonly HttpClient _httpClient;
    private readonly IVideoCache _videoCache;
    #endregion

    #region properties
    public string Name => "understand_video";
    #endregion

    #region methods
    public VideoUnderstandingTool(IVideoCache videoCache)
    {
        _videoCache = videoCache;
        // 超时统一由工具执行器传入的 CancellationToken 控制。
        _httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>通过阿里云百炼理解视频画面和声音，返回分析内容、模型名称和 Token 用量。</summary>
    public async Task<ToolResult> ExecuteAsync(string argumentsJson, ToolContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var arguments = JsonDocument.Parse(argumentsJson);
        var url = arguments.RootElement.GetProperty("url").GetString() ?? "";
        var cacheKey = arguments.RootElement.TryGetProperty("cache_key", out var cacheKeyElement)
            ? cacheKeyElement.GetString() ?? ""
            : "";
        // URL 只用于定位本次请求里的视频，持久缓存按原消息身份区分，不受下载签名变化影响。
        var hasCacheKey = string.IsNullOrWhiteSpace(cacheKey) == false;
        
        // 在实际调用时读取配置，未使用视频理解时不依赖此配置文件。
        var configPath = Path.Combine(ApplicationPaths.RootDirectory, "Config", "videounderstanding.local.json");
        var configJson = await File.ReadAllTextAsync(configPath, cancellationToken);
        var config = JsonSerializer.Deserialize<VideoUnderstandingConfig>(configJson)
                     ?? throw new InvalidDataException($"视频理解配置不能为 null：{configPath}");

        if (hasCacheKey)
        {
            var cachedResult = await _videoCache.TryGetAsync(cacheKey, config.CacheDurationSeconds, cancellationToken);
            if (cachedResult is not null)
            {
                return new ToolResult(cachedResult);
            }
        }
        
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) == false ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("url 必须是 HTTP 或 HTTPS 视频地址。", nameof(argumentsJson));
        }

        var prompt = arguments.RootElement.GetProperty("prompt").GetString();
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);


        // 在工具入口读取实际视频元数据，正文、引用、转发和直接 URL 调用共用输入限制。
        var metadata = await ReadVideoMetadataAsync(url, config.FfprobePath, cancellationToken);
        if (metadata.FileSize > config.MaxVideoSizeMB * 1024L * 1024L)
        {
            return new ToolResult(JsonSerializer.Serialize(new { url, error = $"视频大小超过 {config.MaxVideoSizeMB} MB，请裁剪后重试。" }));
        }

        if (metadata.DurationSeconds > config.MaxVideoDurationSeconds)
        {
            return new ToolResult(JsonSerializer.Serialize(new { url, error = $"视频时长超过 {config.MaxVideoDurationSeconds} 秒，请裁剪后重试。" }));
        }

        if (metadata.FileSize <= 0 || metadata.DurationSeconds <= 0 || double.IsFinite(metadata.DurationSeconds) == false)
        {
            return new ToolResult(JsonSerializer.Serialize(new { url, error = "无法确定视频大小或时长，未调用视频理解，请提供可读取元数据的视频文件。" }));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, config.Endpoint)
        {
            Content = JsonContent.Create(new
            {
                model = config.Model,
                stream = false,
                modalities = new[] { "text" },
                reasoning_effort = config.ReasoningEffort,
                max_tokens = config.MaxOutputTokens,
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

        var resultJson = JsonSerializer.Serialize(new
        {
            url,
            prompt,
            content = choice.GetProperty("message").GetProperty("content").GetString(),
            model = result.GetProperty("model").GetString(),
            requestId = result.GetProperty("id").GetString(),
            finishReason = choice.GetProperty("finish_reason").GetString(),
            usage = result.GetProperty("usage")
        });
        if (hasCacheKey)
        {
            await _videoCache.SaveAsync(cacheKey, resultJson, cancellationToken);
        }
        return new ToolResult(resultJson);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    /// <summary>使用 ffprobe 读取视频 URL 的文件大小和时长，不转码或缓存视频文件。</summary>
    private static async Task<(long FileSize, double DurationSeconds)> ReadVideoMetadataAsync(string url, string ffprobePath, CancellationToken cancellationToken)
    {
        if (Path.IsPathRooted(ffprobePath) == false &&
            (ffprobePath.Contains(Path.DirectorySeparatorChar) || ffprobePath.Contains(Path.AltDirectorySeparatorChar)))
        {
            ffprobePath = Path.GetFullPath(ffprobePath, ApplicationPaths.RootDirectory);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = ffprobePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        // 逐项传参，视频 URL 不经过 Shell 解释，保留其中的签名和特殊字符。
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("error");
        startInfo.ArgumentList.Add("-show_entries");
        startInfo.ArgumentList.Add("format=duration,size");
        startInfo.ArgumentList.Add("-of");
        startInfo.ArgumentList.Add("json");
        startInfo.ArgumentList.Add(url);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 ffprobe。");
        // 同时读取两个输出管道，避免某个管道写满后阻塞子进程退出。
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            var output = await outputTask;
            var error = await errorTask;
            if (process.ExitCode != 0)
            {
                throw new InvalidDataException($"读取视频元数据失败：{error.Trim()}");
            }

            using var document = JsonDocument.Parse(output);
            var fileSize = 0L;
            var durationSeconds = 0d;
            if (document.RootElement.TryGetProperty("format", out var format))
            {
                if (format.TryGetProperty("size", out var size))
                {
                    long.TryParse(size.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out fileSize);
                }

                if (format.TryGetProperty("duration", out var duration))
                {
                    double.TryParse(duration.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out durationSeconds);
                }
            }

            return (fileSize, durationSeconds);
        }
        finally
        {
            // 工具超时或宿主取消时终止 ffprobe，不能只取消等待而留下后台探测进程。
            if (process.HasExited == false)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }
    #endregion
}
