using QQAIAgent.Abstractions;
using QQAIAgent.Infrastructure.Database;

namespace QQAIAgent.Agent.Statistics;

/// <summary>记录请求、模型轮次和实际工具调用的统计信息，接口不接收对话正文、工具参数或结果。</summary>
public sealed class RequestStatistics(IDatabase database)
{
    #region methods
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await database.ExecuteAsync("PRAGMA journal_mode=WAL;", cancellationToken: cancellationToken);
        await database.ExecuteAsync("""
            BEGIN;

            CREATE TABLE IF NOT EXISTS requests (
                request_id TEXT PRIMARY KEY,
                bot_qq INTEGER NOT NULL,
                group_id INTEGER NOT NULL,
                user_id INTEGER NOT NULL,
                started_at TEXT NOT NULL,
                finished_at TEXT,
                duration_ms INTEGER,
                status TEXT NOT NULL,
                error_type TEXT NOT NULL DEFAULT '',
                reply_message_id INTEGER
            );

            CREATE INDEX IF NOT EXISTS ix_requests_group_user_started
                ON requests (group_id, user_id, started_at);

            CREATE TABLE IF NOT EXISTS model_calls (
                call_id TEXT PRIMARY KEY,
                request_id TEXT NOT NULL REFERENCES requests(request_id),
                round INTEGER NOT NULL,
                reasoning_effort TEXT NOT NULL,
                started_at TEXT NOT NULL,
                finished_at TEXT,
                duration_ms INTEGER,
                status TEXT NOT NULL,
                finish_reason TEXT NOT NULL DEFAULT '',
                error_type TEXT NOT NULL DEFAULT '',
                prompt_tokens INTEGER,
                completion_tokens INTEGER,
                total_tokens INTEGER,
                prompt_cache_hit_tokens INTEGER,
                prompt_cache_miss_tokens INTEGER,
                reasoning_tokens INTEGER,
                UNIQUE(request_id, round)
            );

            CREATE TABLE IF NOT EXISTS tool_calls (
                tool_call_id TEXT PRIMARY KEY,
                request_id TEXT NOT NULL REFERENCES requests(request_id),
                model_call_id TEXT NOT NULL REFERENCES model_calls(call_id),
                provider_tool_call_id TEXT NOT NULL,
                call_index INTEGER NOT NULL,
                tool_name TEXT NOT NULL,
                started_at TEXT NOT NULL,
                finished_at TEXT,
                duration_ms INTEGER,
                status TEXT NOT NULL,
                error_type TEXT NOT NULL DEFAULT '',
                UNIQUE(model_call_id, call_index)
            );

            CREATE INDEX IF NOT EXISTS ix_tool_calls_request ON tool_calls (request_id);

            -- 按请求汇总各轮已返回的用量；没有已知用量时保留 NULL。
            CREATE VIEW IF NOT EXISTS request_statistics AS
            SELECT r.*,
                (SELECT COUNT(*) FROM model_calls m WHERE m.request_id = r.request_id) AS model_call_count,
                (SELECT COUNT(*) FROM tool_calls t WHERE t.request_id = r.request_id) AS tool_call_count,
                (SELECT SUM(m.prompt_tokens) FROM model_calls m WHERE m.request_id = r.request_id) AS prompt_tokens,
                (SELECT SUM(m.completion_tokens) FROM model_calls m WHERE m.request_id = r.request_id) AS completion_tokens,
                (SELECT SUM(m.total_tokens) FROM model_calls m WHERE m.request_id = r.request_id) AS total_tokens,
                (SELECT SUM(m.prompt_cache_hit_tokens) FROM model_calls m WHERE m.request_id = r.request_id) AS prompt_cache_hit_tokens,
                (SELECT SUM(m.prompt_cache_miss_tokens) FROM model_calls m WHERE m.request_id = r.request_id) AS prompt_cache_miss_tokens,
                (SELECT SUM(m.reasoning_tokens) FROM model_calls m WHERE m.request_id = r.request_id) AS reasoning_tokens
            FROM requests r;

            CREATE VIEW IF NOT EXISTS tool_usage AS
            SELECT r.bot_qq, r.group_id, r.user_id, t.tool_name,
                COUNT(*) AS call_count,
                SUM(CASE WHEN t.status = 'completed' THEN 1 ELSE 0 END) AS completed_count,
                SUM(CASE WHEN t.status = 'failed' THEN 1 ELSE 0 END) AS failed_count,
                SUM(CASE WHEN t.status = 'timeout' THEN 1 ELSE 0 END) AS timeout_count,
                SUM(CASE WHEN t.status = 'cancelled' THEN 1 ELSE 0 END) AS cancelled_count,
                SUM(CASE WHEN t.status = 'started' THEN 1 ELSE 0 END) AS unfinished_count
            FROM tool_calls t
            JOIN requests r ON r.request_id = t.request_id
            GROUP BY r.bot_qq, r.group_id, r.user_id, t.tool_name;

            COMMIT;
            """, cancellationToken: cancellationToken);
    }

    public async Task<string> StartRequestAsync(long botAccount, long groupId, long userId, CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid().ToString("N");
        await database.ExecuteAsync("""
            INSERT INTO requests (request_id, bot_qq, group_id, user_id, started_at, status)
            VALUES (@RequestId, @BotAccount, @GroupId, @UserId, @StartedAt, 'processing');
            """, new
        {
            RequestId = requestId,
            BotAccount = botAccount,
            GroupId = groupId,
            UserId = userId,
            StartedAt = DateTimeOffset.UtcNow.ToString("O")
        }, cancellationToken);
        return requestId;
    }

    public Task FinishRequestAsync(string requestId, string status, long durationMs, string errorType, CancellationToken cancellationToken)
    {
        return database.ExecuteAsync("""
            UPDATE requests SET finished_at = @FinishedAt, duration_ms = @DurationMs,
                status = @Status, error_type = @ErrorType
            WHERE request_id = @RequestId;
            """, new
        {
            RequestId = requestId,
            FinishedAt = DateTimeOffset.UtcNow.ToString("O"),
            DurationMs = durationMs,
            Status = status,
            ErrorType = errorType
        }, cancellationToken);
    }

    /// <summary>只记录 NapCat 已确认发送的回复 ID，不保存回复文本。</summary>
    public Task RecordReplyAsync(string requestId, long messageId, CancellationToken cancellationToken)
    {
        return database.ExecuteAsync("""
            UPDATE requests SET reply_message_id = @MessageId WHERE request_id = @RequestId;
            """, new { RequestId = requestId, MessageId = messageId }, cancellationToken);
    }

    /// <summary>读取本次请求各轮已返回的 Token 用量汇总，未知用量保留 null。</summary>
    public async Task<(long? PromptTokens, long? CompletionTokens, long? PromptCacheHitTokens)> GetRequestTokenUsageAsync(string requestId, CancellationToken cancellationToken)
    {
        // Dapper 按列顺序映射元组，查询列顺序需与返回值一致。
        var results = await database.QueryAsync<(long? PromptTokens, long? CompletionTokens, long? PromptCacheHitTokens)>("""
            SELECT prompt_tokens, completion_tokens, prompt_cache_hit_tokens
            FROM request_statistics
            WHERE request_id = @RequestId;
            """, new { RequestId = requestId }, cancellationToken);
        return results[0];
    }

    public async Task<string> StartModelCallAsync(string requestId, int round, ReasoningEffort reasoningEffort, CancellationToken cancellationToken)
    {
        var callId = Guid.NewGuid().ToString("N");
        await database.ExecuteAsync("""
            INSERT INTO model_calls (call_id, request_id, round, reasoning_effort, started_at, status)
            VALUES (@CallId, @RequestId, @Round, @ReasoningEffort, @StartedAt, 'started');
            """, new
        {
            CallId = callId,
            RequestId = requestId,
            Round = round,
            ReasoningEffort = reasoningEffort.ToString(),
            StartedAt = DateTimeOffset.UtcNow.ToString("O")
        }, cancellationToken);
        return callId;
    }

    public Task FinishModelCallAsync(string callId, string status, string finishReason, long durationMs, string errorType, CancellationToken cancellationToken,
        long? promptTokens = null, long? completionTokens = null, long? totalTokens = null,
        long? promptCacheHitTokens = null, long? promptCacheMissTokens = null, long? reasoningTokens = null)
    {
        return database.ExecuteAsync("""
            UPDATE model_calls SET finished_at = @FinishedAt, duration_ms = @DurationMs,
                status = @Status, finish_reason = @FinishReason, error_type = @ErrorType,
                prompt_tokens = @PromptTokens, completion_tokens = @CompletionTokens, total_tokens = @TotalTokens,
                prompt_cache_hit_tokens = @PromptCacheHitTokens, prompt_cache_miss_tokens = @PromptCacheMissTokens,
                reasoning_tokens = @ReasoningTokens
            WHERE call_id = @CallId;
            """, new
        {
            CallId = callId,
            FinishedAt = DateTimeOffset.UtcNow.ToString("O"),
            DurationMs = durationMs,
            Status = status,
            FinishReason = finishReason,
            ErrorType = errorType,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            TotalTokens = totalTokens,
            PromptCacheHitTokens = promptCacheHitTokens,
            PromptCacheMissTokens = promptCacheMissTokens,
            ReasoningTokens = reasoningTokens
        }, cancellationToken);
    }

    /// <summary>在实际调用工具前记录一次使用，包含 Agent 的前置调用；主模型未启用、未加载或达到轮次上限的工具不计入。</summary>
    public async Task<string> StartToolCallAsync(string requestId, string modelCallId, string providerToolCallId, int callIndex, string toolName, CancellationToken cancellationToken)
    {
        var toolCallId = Guid.NewGuid().ToString("N");
        await database.ExecuteAsync("""
            INSERT INTO tool_calls (tool_call_id, request_id, model_call_id, provider_tool_call_id,
                call_index, tool_name, started_at, status)
            VALUES (@ToolCallId, @RequestId, @ModelCallId, @ProviderToolCallId,
                @CallIndex, @ToolName, @StartedAt, 'started');
            """, new
        {
            ToolCallId = toolCallId,
            RequestId = requestId,
            ModelCallId = modelCallId,
            ProviderToolCallId = providerToolCallId,
            CallIndex = callIndex,
            ToolName = toolName,
            StartedAt = DateTimeOffset.UtcNow.ToString("O")
        }, cancellationToken);
        return toolCallId;
    }

    public Task FinishToolCallAsync(string toolCallId, string status, long durationMs, string errorType, CancellationToken cancellationToken)
    {
        return database.ExecuteAsync("""
            UPDATE tool_calls SET finished_at = @FinishedAt, duration_ms = @DurationMs,
                status = @Status, error_type = @ErrorType
            WHERE tool_call_id = @ToolCallId;
            """, new
        {
            ToolCallId = toolCallId,
            FinishedAt = DateTimeOffset.UtcNow.ToString("O"),
            DurationMs = durationMs,
            Status = status,
            ErrorType = errorType
        }, cancellationToken);
    }
    #endregion
}
