using QQAIAgent.Agent.Config;
using QQAIAgent.Messaging;
using QQAIAgent.Schedule;

namespace QQAIAgent.Host.Schedule;

public sealed class SendGroupMessageHandler(IMessageSender messageSender, AgentConfig agentConfig, ILogger<SendGroupMessageHandler> logger) : IScheduleHandler
{
    #region properties
    public string FunctionName => "SendGroupMessage";
    #endregion

    #region methods
    public async Task ExecuteAsync(ScheduleTask task, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(task.Parameter))
        {
            return;
        }

        // 仅单独配置 -1 时使用 AgentConfig 配置的允许群，排除列表对所有目标群生效。
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

            // 只在实际发送的目标群之间等待，首群和被排除的群不增加等待时间。
            if (hasAttemptedSend)
            {
                await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(3, 11)), cancellationToken);
            }

            hasAttemptedSend = true;
            try
            {
                var messageId = await messageSender.SendGroupMessageAsync(groupId, 0, task.Parameter,
                    quoteMessageId: null, atUser: false, cancellationToken: cancellationToken);
                logger.LogInformation("定时消息发送成功：任务={TaskId}，群={GroupId}，消息ID={MessageId}", task.Id, groupId, messageId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // 单个群发送失败后继续处理其他目标群，不自动重试。
                logger.LogWarning(exception, "定时消息发送未完成：任务={TaskId}，群={GroupId}", task.Id, groupId);
            }
        }
    }
    #endregion
}
