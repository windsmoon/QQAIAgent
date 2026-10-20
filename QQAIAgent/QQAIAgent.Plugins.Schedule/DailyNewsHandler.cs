using System.Text.Json;
using Microsoft.Extensions.Logging;
using QQAIAgent.Agent.Config;
using QQAIAgent.Messaging;
using QQAIAgent.Schedule;

namespace QQAIAgent.Plugins.Schedule;

public sealed class DailyNewsHandler(IMessageSender messageSender, AgentConfig agentConfig, ILogger<DailyNewsHandler> logger) : IScheduleHandler, IDisposable
{
    #region fields
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    #endregion

    #region properties
    public string FunctionName => "SendDailyNews";
    #endregion

    #region methods
    public async Task ExecuteAsync(ScheduleTask task, CancellationToken cancellationToken)
    {
        var groupIds = task.GroupIds.Count == 1 && task.GroupIds[0] == -1
            ? agentConfig.AllowedGroups
            : task.GroupIds;
        if (groupIds.Any(groupId => task.ExcludedGroupIds.Contains(groupId) == false) == false)
        {
            return;
        }

        // 每次任务执行只请求一次新闻接口，各群发送本次响应中的同一张图片。
        var json = await _httpClient.GetStringAsync($"{task.Parameter}?encoding=json", cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("code").GetInt32() != 200)
        {
            throw new InvalidOperationException($"每日新闻接口获取失败：{root.GetProperty("message").GetString()}");
        }

        var imageUrl = root.GetProperty("data").GetProperty("image").GetString();
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            throw new InvalidDataException("每日新闻接口返回的图片地址为空。");
        }

        var hasAttemptedSend = false;
        foreach (var groupId in groupIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (task.ExcludedGroupIds.Contains(groupId))
            {
                continue;
            }

            // 与文本任务保持一致，只在实际发送的群之间随机等待。
            if (hasAttemptedSend)
            {
                await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(3, 11)), cancellationToken);
            }

            hasAttemptedSend = true;
            try
            {
                var messageId = await messageSender.SendGroupImageAsync(groupId, imageUrl, cancellationToken);
                logger.LogInformation("定时图片发送成功：任务={TaskId}，群={GroupId}，消息ID={MessageId}", task.Id, groupId, messageId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // 单个群发送失败后继续处理其他目标群，不自动重试。
                logger.LogWarning(exception, "定时图片发送未完成：任务={TaskId}，群={GroupId}", task.Id, groupId);
            }
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
    #endregion
}
