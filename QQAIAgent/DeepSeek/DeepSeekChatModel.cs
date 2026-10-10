using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using QQAIAgent.DeepSeek.Config;
using QQAIAgent.Agent;
using QQAIAgent.Agent.Chat;

namespace QQAIAgent.DeepSeek;

public sealed class DeepSeekChatModel : IChatModel, IDisposable
{
    #region fields
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly Uri _endpoint;
    private readonly TimeSpan _timeout;
    #endregion

    #region properties
    public bool SupportsToolCalling => true;
    #endregion

    #region methods
    public DeepSeekChatModel()
    {
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

            var serializedMessage = new Dictionary<string, object>
            {
                ["role"] = role,
                ["content"] = message.Content
            };
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
            else if (string.IsNullOrEmpty(message.Content))
            {
                throw new ArgumentException("纯文本消息内容不能为空。", nameof(request));
            }

            messages.Add(serializedMessage);
        }

        var requestBody = new Dictionary<string, object>
        {
            ["model"] = _model,
            ["messages"] = messages,
            ["max_tokens"] = request.MaxOutputTokens,
            ["stream"] = false,
            ["thinking"] = new { type = "disabled" }
        };
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

        return new ChatResponse
        {
            Message = new ChatMessage { Role = ChatRole.Assistant, Content = text, ToolCalls = toolCalls },
            FinishReason = finishReason
        };
    }
    #endregion
}
