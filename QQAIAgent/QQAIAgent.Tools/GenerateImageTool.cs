using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using QQAIAgent.Abstractions.Tool;
using QQAIAgent.Infrastructure.Runtime;
using QQAIAgent.Messaging;
using QQAIAgent.Tools.Config;

namespace QQAIAgent.Tools;

public sealed class GenerateImageTool : ITool, IDisposable
{
    #region fields
    private readonly HttpClient _httpClient;
    private readonly IMessageSender _messageSender;
    #endregion

    #region properties
    public string Name => "generate_image";
    #endregion

    #region methods
    public GenerateImageTool(IMessageSender messageSender)
    {
        _messageSender = messageSender;
        // 生图耗时较长，超时统一由工具执行器传入的 CancellationToken 控制。
        _httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>通过百炼生成一张图片并发送到当前群，返回图片地址和发送结果。</summary>
    public async Task<ToolResult> ExecuteAsync(string argumentsJson, ToolContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var arguments = JsonDocument.Parse(argumentsJson);
        var prompt = arguments.RootElement.GetProperty("prompt").GetString();
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        var model = arguments.RootElement.TryGetProperty("model", out var modelElement)
            ? modelElement.GetString()
            : "qwen-image-2.1-turbo";
        if (model is not ("qwen-image-2.1-turbo" or "qwen-image-3.0"))
        {
            throw new ArgumentException("model 仅支持 qwen-image-2.1-turbo 或 qwen-image-3.0。", nameof(argumentsJson));
        }

        var size = arguments.RootElement.TryGetProperty("size", out var sizeElement)
            ? sizeElement.GetString()
            : "auto";

        // 在实际调用时读取配置，未使用生图时不依赖此配置文件。
        var configPath = Path.Combine(ApplicationPaths.RootDirectory, "Config", "imagegeneration.local.json");
        var configJson = await File.ReadAllTextAsync(configPath, cancellationToken);
        var config = JsonSerializer.Deserialize<ImageGenerationConfig>(configJson)
            ?? throw new InvalidDataException($"生图配置不能为 null：{configPath}");

        using var request = new HttpRequestMessage(HttpMethod.Post, config.Endpoint)
        {
            // 使用 OpenAI Images 兼容协议，尺寸分隔符为 x；只取 URL，不在宿主中加载图片字节。
            Content = JsonContent.Create(new { model, prompt, size, n = 1 })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var responseDocument = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        var result = responseDocument.RootElement;
        if (result.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException($"百炼生图失败：{error.GetProperty("code").GetString()}，{error.GetProperty("message").GetString()}");
        }

        var url = result.GetProperty("data")[0].GetProperty("url").GetString();
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new InvalidDataException("百炼生图成功响应中缺少图片地址。");
        }

        var usage = result.GetProperty("usage");

        long messageId;
        try
        {
            // 目标群只能来自请求上下文，不能由模型指定其他群。
            messageId = await _messageSender.SendGroupImageAsync(context.GroupId, url, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException && cancellationToken.IsCancellationRequested == false)
        {
            // 生图已计费，发送失败时保留 URL，避免模型误以为生图失败而重复生成。
            return new ToolResult(JsonSerializer.Serialize(new
            {
                model,
                url,
                generated = true,
                sent = false,
                error = $"图片已生成，但群图片发送失败：{exception.Message}。请将图片链接交给用户，不要重新生成。"
            }));
        }

        return new ToolResult(JsonSerializer.Serialize(new
        {
            model,
            url,
            generated = true,
            sent = true,
            messageId,
            usage
        }));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
    #endregion
}
