using Microsoft.Extensions.Logging;
using QQAIAgent.Agent.Config;
using QQAIAgent.Messaging;
using QQAIAgent.Schedule;

namespace QQAIAgent.Plugins.Schedule;

public sealed class DailyNewsHandler(IMessageSender messageSender, AgentConfig agentConfig, ILogger<DailyNewsHandler> logger) : IScheduleHandler
{
    #region properties
    public string FunctionName => "SendDailyNews";
    #endregion

    #region methods
    public async Task ExecuteAsync(ScheduleTask task, CancellationToken cancellationToken)
    {
        var groupIds = task.GroupIds.Count == 1 && task.GroupIds[0] == -1
            ? agentConfig.AllowedGroups
            : task.GroupIds;
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
                // var messageId = await messageSender.SendGroupImageAsync(groupId, task.ImageUrl, cancellationToken);
                // logger.LogInformation("定时图片发送成功：任务={TaskId}，群={GroupId}，消息ID={MessageId}", task.Id, groupId, messageId);
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
    #endregion
}
