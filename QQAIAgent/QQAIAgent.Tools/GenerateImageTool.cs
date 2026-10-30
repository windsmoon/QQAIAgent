using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using QQAIAgent.Abstractions.Tool;
using QQAIAgent.Infrastructure.Messages;
using QQAIAgent.Infrastructure.Runtime;
using QQAIAgent.Messaging;
using QQAIAgent.Messaging.Content;
using QQAIAgent.Tools.Config;

namespace QQAIAgent.Tools;

public sealed class GenerateImageTool : ITool, IDisposable
{
    #region fields
    private readonly HttpClient _httpClient;
    private readonly IMessageSender _messageSender;
    private readonly IMessageReader _messageReader;
    private readonly GroupMessageStore _groupMessageStore;
    private readonly RkeyCache _rkeyCache;
    #endregion

    #region properties
    public string Name => "generate_image";
    #endregion

    #region methods
    public GenerateImageTool(IMessageSender messageSender, IMessageReader messageReader, GroupMessageStore groupMessageStore, RkeyCache rkeyCache)
    {
        _messageSender = messageSender;
        _messageReader = messageReader;
        _groupMessageStore = groupMessageStore;
        _rkeyCache = rkeyCache;
        // 生图耗时较长，超时统一由工具执行器传入的 CancellationToken 控制。
        _httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>通过百炼按文字或当前群消息中的参考图生成一张图片并发送到当前群。</summary>
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

        var requestBody = new Dictionary<string, object>
        {
            ["model"] = model,
            ["prompt"] = prompt,
            ["size"] = size,
            ["n"] = 1
        };
        if (arguments.RootElement.TryGetProperty("reference_images", out var referenceImages))
        {
            var maxImages = model == "qwen-image-2.1-turbo" ? 10 : 3;
            if (referenceImages.ValueKind != JsonValueKind.Array || referenceImages.GetArrayLength() < 1 || referenceImages.GetArrayLength() > maxImages)
            {
                throw new ArgumentException($"{model} 的 reference_images 必须包含 1 至 {maxImages} 张参考图。", nameof(argumentsJson));
            }

            var imageUrls = new List<string>(referenceImages.GetArrayLength());
            var sourceMessages = new Dictionary<long, GroupMessage>();
            foreach (var reference in referenceImages.EnumerateArray())
            {
                var sourceMessageId = reference.GetProperty("message_id").GetInt64();
                var imageIndex = reference.GetProperty("image_index").GetInt32();
                if (imageIndex < 0)
                {
                    throw new ArgumentException("image_index 必须大于或等于 0。", nameof(argumentsJson));
                }

                if (sourceMessages.TryGetValue(sourceMessageId, out var sourceMessage) == false)
                {
                    // 新消息可能尚未入库；沿用引用消息的读取顺序，未命中时从 NapCat 取得。
                    sourceMessage = await _groupMessageStore.GetMessageAsync(context.GroupId, sourceMessageId, cancellationToken)
                        ?? await _messageReader.GetGroupMessageAsync(sourceMessageId, cancellationToken);
                    sourceMessages.Add(sourceMessageId, sourceMessage);
                }

                var sourceImage = sourceMessage.MessageContentList.OfType<ImageContent>().FirstOrDefault(image => image.ImageIndex == imageIndex);
                if (sourceImage is null)
                {
                    throw new ArgumentException($"消息 {sourceMessageId} 中不存在 image_index={imageIndex} 的图片。", nameof(argumentsJson));
                }

                if (Uri.TryCreate(sourceImage.Url, UriKind.Absolute, out var imageUri) == false ||
                    (imageUri.Scheme != Uri.UriSchemeHttp && imageUri.Scheme != Uri.UriSchemeHttps))
                {
                    throw new InvalidDataException($"消息 {sourceMessageId} 的图片 {imageIndex} 缺少 HTTP/HTTPS 图片地址。");
                }

                // 使用原图地址并刷新 QQ 下载凭据，不使用 DeepSeek 上传后的 file_id；数组顺序对应提示词中的图1、图2。
                imageUrls.Add(await _rkeyCache.GetImageDownloadUrlAsync(sourceImage.Url, cancellationToken));
            }

            // 文生图必须省略 image，不能发送空数组或 null。
            requestBody["image"] = imageUrls;
        }

        // 在实际调用时读取配置，未使用生图时不依赖此配置文件。
        var configPath = Path.Combine(ApplicationPaths.RootDirectory, "Config", "imagegeneration.local.json");
        var configJson = await File.ReadAllTextAsync(configPath, cancellationToken);
        var config = JsonSerializer.Deserialize<ImageGenerationConfig>(configJson)
            ?? throw new InvalidDataException($"生图配置不能为 null：{configPath}");

        using var request = new HttpRequestMessage(HttpMethod.Post, config.Endpoint)
        {
            // 使用 OpenAI Images 兼容协议，尺寸分隔符为 x；只取 URL，不在宿主中加载图片字节。
            Content = JsonContent.Create(requestBody)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.IsSuccessStatusCode == false)
        {
            // 保留百炼返回的完整错误体；非 JSON 的网关错误也能直接查看。
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"百炼生图请求失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}，{errorBody}",
                null, response.StatusCode);
        }

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
