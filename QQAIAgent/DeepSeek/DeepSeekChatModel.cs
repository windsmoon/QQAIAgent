using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DeepSeek.Config;
using QQAIAgent.Agent;
using QQAIAgent.Agent.Chat;

namespace DeepSeek;

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
    public bool SupportsToolCalling => false;
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

        if (request.Tools.Count > 0)
        {
            throw new NotSupportedException("当前 DeepSeek 实现不支持工具调用。");
        }

        if (request.MaxOutputTokens is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "MaxOutputTokens 必须大于零。");
        }

        var messages = new List<object>(request.Messages.Count);
        foreach (var message in request.Messages)
        {
            if (message is null || message.ToolCalls.Count > 0 || string.IsNullOrEmpty(message.ToolCallId) == false)
            {
                throw new NotSupportedException("当前 DeepSeek 实现只接受纯文本对话功能。");
            }

            var role = message.Role switch
            {
                ChatRole.System => "system",
                ChatRole.User => "user",
                ChatRole.Assistant => "assistant",
                _ => throw new NotSupportedException($"当前 DeepSeek 实现不支持 {message.Role} 角色。")
            };

            if (string.IsNullOrEmpty(message.Content))
            {
                throw new ArgumentException("纯文本消息内容不能为空。", nameof(request));
            }

            messages.Add(new { role, content = message.Content });
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = JsonContent.Create(new
            {
                model = _model,
                messages,
                max_tokens = request.MaxOutputTokens,
                stream = false,
                thinking = new { type = "disabled" }
            })
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
        if (finishReason == ChatFinishReason.ToolCalls)
        {
            throw new NotSupportedException("DeepSeek 返回了当前实现不支持的工具调用。");
        }

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
        if ((finishReason is ChatFinishReason.Finish or ChatFinishReason.Length) && string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidDataException("DeepSeek 响应没有可用的文本内容。");
        }

        return new ChatResponse
        {
            Message = new ChatMessage { Role = ChatRole.Assistant, Content = text },
            FinishReason = finishReason
        };
    }
    #endregion
}
