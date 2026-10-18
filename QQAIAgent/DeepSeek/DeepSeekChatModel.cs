using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using QQAIAgent.Abstractions;
using QQAIAgent.DeepSeek.Config;
using QQAIAgent.Agent;
using QQAIAgent.Agent.Chat;
using QQAIAgent.Agent.Chat.Content;
using QQAIAgent.Infrastructure.Image;
using QQAIAgent.Messaging;

namespace QQAIAgent.DeepSeek;

public sealed class DeepSeekChatModel : IChatModel, IDisposable
{
    #region fields
    private const string StorageSource = "DeepSeek";
    private readonly HttpClient _httpClient;
    private readonly ImageFileStore _imageFilStore;
    private readonly ILogger<DeepSeekChatModel> _logger;
    private readonly IMessageReader _messageReader;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly Uri _endpoint;
    private readonly Uri _filesEndpoint;
    private readonly TimeSpan _timeout;
    #endregion

    #region properties
    public bool SupportsToolCalling => true;
    #endregion

    #region methods
    public DeepSeekChatModel(ImageFileStore imageFilStore, ILogger<DeepSeekChatModel> logger, IMessageReader messageReader)
    {
        _imageFilStore = imageFilStore;
        _logger = logger;
        _messageReader = messageReader;
        var configPath = Path.Combine(AppContext.BaseDirectory, "Config", "deepseek.local.json");
        var configJson = File.ReadAllText(configPath);
        var config = JsonSerializer.Deserialize<DeepSeekConfig>(configJson)
            ?? throw new InvalidDataException($"DeepSeek 配置不能为 null：{configPath}");

        ArgumentException.ThrowIfNullOrWhiteSpace(config.ApiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(config.Model);
        if (config.TimeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(config), "TimeoutSeconds 必须大于零。");
        }

        if (Uri.TryCreate(config.Endpoint, UriKind.Absolute, out var endpoint) == false || endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("DeepSeek Endpoint 必须是 HTTPS 绝对地址。", nameof(config));
        }

        _apiKey = config.ApiKey;
        _model = config.Model;
        _endpoint = endpoint;
        // 与 chat/completions 使用相同的 API 路径前缀，包括可选的 v1 前缀。
        _filesEndpoint = new Uri(endpoint, "../files");
        _timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
        _httpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<ChatResponse> CompleteAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Messages is not { Count: > 0 })
        {
            throw new ArgumentException("模型请求必须包含至少一条消息。", nameof(request));
        }

        if (request.MaxOutputTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "MaxOutputTokens 必须大于零。");
        }

        var messages = new List<object>(request.Messages.Count);
        var uploadedFiles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var message in request.Messages)
        {
            if (message is null)
            {
                throw new ArgumentException("请求消息不能为 null。", nameof(request));
            }

            var role = message.Role switch
            {
                ChatRole.System => "system",
                ChatRole.User => "user",
                ChatRole.Assistant => "assistant",
                ChatRole.Tool => "tool",
                _ => throw new NotSupportedException($"当前 DeepSeek 实现不支持 {message.Role} 角色。")
            };

            object serializedContent;
            if (message.ContentList.Any(content => content is ChatImageContent))
            {
                if (message.Role != ChatRole.User)
                {
                    throw new NotSupportedException("DeepSeek 图片输入仅支持 User 消息。");
                }

                var contentParts = new List<object>(message.ContentList.Count);
                for (var contentIndex = 0; contentIndex < message.ContentList.Count; contentIndex++)
                {
                    var content = message.ContentList[contentIndex];
                    switch (content)
                    {
                        case ChatTextContent textContent:
                            contentParts.Add(new { type = "text", text = textContent.Text });
                            break;
                        case ChatImageContent imageContent:
                            try
                            {
                                if (string.IsNullOrEmpty(imageContent.FileId))
                                {
                                    if (uploadedFiles.TryGetValue(imageContent.Url, out var fileId) == false)
                                    {
                                        fileId = await _imageFilStore.FindValidFileIdAsync(StorageSource, imageContent.Url, cancellationToken);
                                        if (string.IsNullOrEmpty(fileId))
                                        {
                                            fileId = await UploadImageAsync(imageContent.Url, cancellationToken);
                                            Task.Run(() => _imageFilStore.RecordAsync(StorageSource, imageContent.Url, fileId, TimeSpan.FromDays(29), CancellationToken.None));
                                        }
                                    }

                                    imageContent.FileId = fileId;
                                }
                            }
                            catch (Exception exception) when (cancellationToken.IsCancellationRequested == false)
                            {
                                _logger.LogWarning("图片处理失败，已替换为文字标记：图片地址={ImageUrl}，原因={Reason}",
                                    imageContent.Url, exception.Message);
                                imageContent.FileId = string.Empty;
                            }

                            if (string.IsNullOrEmpty(imageContent.Url) == false)
                            {
                                uploadedFiles[imageContent.Url] = imageContent.FileId;
                            }
                            if (string.IsNullOrEmpty(imageContent.FileId))
                            {
                                // 失败结果保留在本次请求的消息中，避免后续工具调用轮次重复读取同一张图片。
                                var fallback = new ChatTextContent { Text = "[图片读取失败]" };
                                message.ContentList[contentIndex] = fallback;
                                contentParts.Add(new { type = "text", text = fallback.Text });
                                break;
                            }
                            contentParts.Add(new { type = "file", file_id = imageContent.FileId });
                            break;
                        default:
                            throw new NotSupportedException("当前 DeepSeek 适配仅支持文本和图片内容。");
                    }
                }

                serializedContent = contentParts;
            }
            else
            {
                serializedContent = string.Concat(message.ContentList.Select(content => content switch
                {
                    ChatTextContent textContent => textContent.Text,
                    _ => throw new NotSupportedException("当前 DeepSeek 适配仅支持文本和图片内容。")
                }));
            }

            var serializedMessage = new Dictionary<string, object>
            {
                ["role"] = role,
                ["content"] = serializedContent
            };
            if (message.Role == ChatRole.Assistant && request.Tools.Count > 0)
            {
                // 携带工具时需回传思考内容；从非思考模式切换时，旧消息没有思考内容则传空串。
                serializedMessage["reasoning_content"] = message.ReasoningContent ?? string.Empty;
            }
            if (message is { Role: ChatRole.Assistant, ToolCalls.Count: > 0 })
            {
                // 带工具调用的 assistant 消息可以没有文本，但必须保留调用 ID 和参数。
                serializedMessage["tool_calls"] = message.ToolCalls.Select(toolCall => new
                {
                    id = toolCall.Id,
                    type = "function",
                    function = new
                    {
                        name = toolCall.Name,
                        arguments = toolCall.ArgumentsJson
                    }
                }).ToList();
            }
            else if (message.Role == ChatRole.Tool)
            {
                serializedMessage["tool_call_id"] = message.ToolCallId;
            }
            else if (message.Role != ChatRole.User && serializedContent is string text && string.IsNullOrEmpty(text))
            {
                throw new ArgumentException("非 User 消息，纯文本消息内容不能为空。", nameof(request));
            }

            messages.Add(serializedMessage);
        }

        // 统一档位在模型类库内映射；DeepSeek 的 medium 与 high 使用同一实际档位。
        var reasoningEffort = request.ReasoningEffort switch
        {
            ReasoningEffort.Minimal => null,
            ReasoningEffort.Low => "low",
            ReasoningEffort.Medium or ReasoningEffort.High => "high",
            ReasoningEffort.Max => "max",
            _ => throw new ArgumentOutOfRangeException(nameof(request), "不支持的思考强度。")
        };
        var requestBody = new Dictionary<string, object>
        {
            ["model"] = _model,
            ["messages"] = messages,
            ["max_tokens"] = request.MaxOutputTokens,
            ["stream"] = false,
            ["thinking"] = new { type = reasoningEffort is null ? "disabled" : "enabled" }
        };
        if (reasoningEffort is not null)
        {
            requestBody["reasoning_effort"] = reasoningEffort;
        }
        if (request.Tools.Count > 0)
        {
            var tools = new List<object>(request.Tools.Count);
            foreach (var tool in request.Tools)
            {
                var function = new Dictionary<string, object?>
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description
                };
                if (string.IsNullOrWhiteSpace(tool.ParametersJson) == false)
                {
                    // parameters 需要发送 JSON 对象，不能直接发送配置中的 JSON 字符串。
                    function["parameters"] = JsonSerializer.Deserialize<JsonElement>(tool.ParametersJson);
                }

                tools.Add(new { type = "function", function });
            }

            requestBody["tools"] = tools;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = JsonContent.Create(requestBody)
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var httpResponse = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        httpResponse.EnsureSuccessStatusCode();
        await using var responseStream = await httpResponse.Content.ReadAsStreamAsync(timeout.Token);
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: timeout.Token);
        return ParseResponse(document.RootElement);
    }

    /// <summary>流式读取并上传图片，文件自创建起保留 30 天，下载和上传共用单次超时。</summary>
    private async Task<string> UploadImageAsync(string imageUrl, CancellationToken cancellationToken, bool refreshExpiredUrl = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUrl);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        // API 密钥只加在上传请求上，读取图片来源时不携带。
        using var imageResponse = await _httpClient.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (imageResponse.IsSuccessStatusCode == false)
        {
            var errorBody = await imageResponse.Content.ReadAsStringAsync(timeout.Token);
            if (imageResponse.StatusCode == System.Net.HttpStatusCode.BadRequest &&
                errorBody.Contains("download url has expired", StringComparison.OrdinalIgnoreCase) &&
                refreshExpiredUrl && new Uri(imageUrl).Host == "multimedia.nt.qq.com.cn")
            {
                // 只刷新一次，重试仍受原图片超时约束；成功后由调用方按旧地址缓存文件 ID。
                _logger.LogInformation("图片下载地址已过期，正在通过 NapCat 刷新凭据。");
                var refreshedUrl = await _messageReader.RefreshImageUrlAsync(imageUrl, timeout.Token);
                var refreshedFileId = await UploadImageAsync(refreshedUrl, timeout.Token, refreshExpiredUrl: false);
                _logger.LogInformation("图片凭据刷新后上传成功。");
                return refreshedFileId;
            }
            throw new HttpRequestException($"下载原始图片失败：HTTP={(int)imageResponse.StatusCode}，响应={errorBody[..Math.Min(errorBody.Length, 2048)]}",
                null, imageResponse.StatusCode);
        }
        await using var imageStream = await imageResponse.Content.ReadAsStreamAsync(timeout.Token);
        using var uploadContent = new MultipartFormDataContent();
        // 直接转发响应流，不落盘、不生成完整字节数组或 Base64 字符串。
        uploadContent.Add(new StreamContent(imageStream), "file", "image");
        uploadContent.Add(new StringContent("user_data"), "purpose");
        uploadContent.Add(new StringContent("created_at"), "expires_after[anchor]");
        uploadContent.Add(new StringContent("2592000"), "expires_after[seconds]");

        using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, _filesEndpoint)
        {
            Content = uploadContent
        };
        uploadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        using var uploadResponse = await _httpClient.SendAsync(uploadRequest, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (uploadResponse.IsSuccessStatusCode == false)
        {
            var errorBody = await uploadResponse.Content.ReadAsStringAsync(timeout.Token);
            throw new HttpRequestException($"上传图片到 DeepSeek 失败：HTTP={(int)uploadResponse.StatusCode}，响应={errorBody[..Math.Min(errorBody.Length, 2048)]}",
                null, uploadResponse.StatusCode);
        }
        await using var responseStream = await uploadResponse.Content.ReadAsStreamAsync(timeout.Token);
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: timeout.Token);
        var fileId = document.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
            ? id.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(fileId))
        {
            throw new InvalidDataException("DeepSeek 文件上传响应缺少有效的文件 ID。");
        }

        return fileId;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static ChatResponse ParseResponse(JsonElement root)
    {
        if (root.TryGetProperty("choices", out var choices) == false || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
        {
            throw new InvalidDataException("DeepSeek 响应缺少 choices。");
        }

        var choice = choices[0];
        if (choice.TryGetProperty("finish_reason", out var finishValue) == false || finishValue.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("DeepSeek 响应缺少 finish_reason。");
        }

        var finishReason = finishValue.GetString() switch
        {
            "stop" => ChatFinishReason.Finish,
            "length" => ChatFinishReason.Length,
            "tool_calls" => ChatFinishReason.ToolCalls,
            "content_filter" => ChatFinishReason.ContentFilter,
            "aborted" or "insufficient_system_resource" => ChatFinishReason.Interrupted,
            _ => ChatFinishReason.Unknown
        };
        if (choice.TryGetProperty("message", out var responseMessage) == false
            || responseMessage.ValueKind != JsonValueKind.Object
            || responseMessage.TryGetProperty("role", out var role) == false
            || role.ValueKind != JsonValueKind.String
            || role.GetString() != "assistant"
            || responseMessage.TryGetProperty("content", out var content) == false
            || content.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
        {
            throw new InvalidDataException("DeepSeek 响应缺少有效的 assistant 消息。");
        }

        var text = content.GetString() ?? "";
        var reasoningContent = responseMessage.TryGetProperty("reasoning_content", out var reasoning)
            && reasoning.ValueKind == JsonValueKind.String
                ? reasoning.GetString()
                : null;
        var toolCalls = new List<ChatToolCall>();
        if (responseMessage.TryGetProperty("tool_calls", out var toolCallValues) && toolCallValues.ValueKind != JsonValueKind.Null)
        {
            if (toolCallValues.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("DeepSeek 响应中的 tool_calls 必须是数组。");
            }

            foreach (var toolCall in toolCallValues.EnumerateArray())
            {
                if (toolCall.ValueKind != JsonValueKind.Object
                    || toolCall.TryGetProperty("id", out var id) == false
                    || id.ValueKind != JsonValueKind.String
                    || toolCall.TryGetProperty("type", out var type) == false
                    || type.ValueKind != JsonValueKind.String
                    || type.GetString() != "function"
                    || toolCall.TryGetProperty("function", out var function) == false
                    || function.ValueKind != JsonValueKind.Object
                    || function.TryGetProperty("name", out var name) == false
                    || name.ValueKind != JsonValueKind.String
                    || function.TryGetProperty("arguments", out var arguments) == false
                    || arguments.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidDataException("DeepSeek 响应缺少有效的 function 工具调用。");
                }

                // 参数保留模型返回的字符串，由工具执行阶段负责解析。
                toolCalls.Add(new ChatToolCall
                {
                    Id = id.GetString() ?? string.Empty,
                    Name = name.GetString() ?? string.Empty,
                    ArgumentsJson = arguments.GetString() ?? string.Empty
                });
            }
        }

        if (finishReason == ChatFinishReason.ToolCalls && toolCalls.Count == 0)
        {
            throw new InvalidDataException("DeepSeek 工具调用响应没有可用的 tool_calls。");
        }

        if ((finishReason is ChatFinishReason.Finish or ChatFinishReason.Length) && string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidDataException("DeepSeek 响应没有可用的文本内容。");
        }

        var response = new ChatResponse
        {
            Message = new ChatMessage
            {
                Role = ChatRole.Assistant,
                ContentList = [new ChatTextContent { Text = text }],
                ReasoningContent = reasoningContent,
                ToolCalls = toolCalls
            },
            FinishReason = finishReason
        };

        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            response.PromptTokens = ReadTokenCount(usage, "prompt_tokens");
            response.CompletionTokens = ReadTokenCount(usage, "completion_tokens");
            response.TotalTokens = ReadTokenCount(usage, "total_tokens");
            response.PromptCacheHitTokens = ReadTokenCount(usage, "prompt_cache_hit_tokens");
            response.PromptCacheMissTokens = ReadTokenCount(usage, "prompt_cache_miss_tokens");
            if (usage.TryGetProperty("completion_tokens_details", out var completionDetails)
                && completionDetails.ValueKind == JsonValueKind.Object)
            {
                response.ReasoningTokens = ReadTokenCount(completionDetails, "reasoning_tokens");
            }
        }

        return response;
    }

    private static long? ReadTokenCount(JsonElement usage, string propertyName)
    {
        return usage.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : null;
    }
    #endregion
}
