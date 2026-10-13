using System.Diagnostics;
using System.Globalization;
using System.Threading.Channels;
using QQAIAgent.Agent;
using QQAIAgent.Agent.Config;
using QQAIAgent.Infrastructure.Messages;
using QQAIAgent.Messaging;
using QQAIAgent.Messaging.Content;
using QQAIAgent.NapCat;
using QQAIAgent.NapCat.Config;

namespace QQAIAgent.Host;

public class Worker(NapCatClient napCatClient, GroupMessageAgent messageAgent, GroupMessageStore groupMessageStore, AgentConfig agentConfig, NapCatConfig napCatConfig, ILogger<Worker> logger, IHostApplicationLifetime applicationLifetime) : BackgroundService
{
    #region fields
    private readonly Channel<(GroupMessage Message, long ReceivedTimestamp)> _messages = Channel.CreateBounded<(GroupMessage Message, long ReceivedAt)>(new BoundedChannelOptions(50)
    {
        SingleReader = false,
        SingleWriter = true,
        FullMode = BoundedChannelFullMode.Wait
    });
    #endregion

    #region methods
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var processingCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        Task? processingTask = null;
        Task? disconnectTask = null;
        napCatClient.GroupMessageReceived += OnGroupMessageReceived;
        try
        {
            using (var startupTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
            {
                startupTimeout.CancelAfter(TimeSpan.FromSeconds(napCatConfig.StartupTimeoutSeconds));
                try
                {
                    logger.LogInformation("正在连接 NapCat WebSocket 服务。");
                    await napCatClient.ConnectAsync(startupTimeout.Token);
                    logger.LogInformation("NapCat WebSocket 已连接，正在核验账号。");

                    await napCatClient.VerifyAccountAsync(startupTimeout.Token);
                    logger.LogInformation("NapCat 登录账号核验通过。");
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested == false)
                {
                    throw new TimeoutException($"NapCat 连接或账号核验超时（{napCatConfig.StartupTimeoutSeconds} 秒）。");
                }
            }

            // 未配置允许的群时保留一个空闲消费者，避免处理任务立即完成而停止宿主。
            processingTask = Task.WhenAll(Enumerable.Range(0, Math.Max(1, agentConfig.AllowedGroups.Count))
                .Select(_ => ProcessMessagesAsync(processingCancellation.Token)));
            disconnectTask = napCatClient.WaitForDisconnectAsync(processingCancellation.Token);
            var completedTask = await Task.WhenAny(processingTask, disconnectTask);
            await completedTask;
            if (stoppingToken.IsCancellationRequested == false)
            {
                logger.LogWarning("NapCat 已关闭连接，正在停止宿主。");
                applicationLifetime.StopApplication();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 按下 Ctrl+C 时取消连接或接收操作，属于正常退出流程。
        }
        finally
        {
            napCatClient.GroupMessageReceived -= OnGroupMessageReceived;
            processingCancellation.Cancel();
            _messages.Writer.TryComplete();
            try
            {
                if (processingTask is not null && disconnectTask is not null)
                {
                    await Task.WhenAll(processingTask, disconnectTask);
                }
            }
            catch (OperationCanceledException) when (processingCancellation.IsCancellationRequested)
            {
                // 正常退出时取消消息处理任务和等待连接断开的任务。
            }
            finally
            {
                await napCatClient.DisconnectAsync();
                logger.LogInformation("NapCat 后台服务已停止。");
            }
        }
    }

    private async Task RecordGroupMessageAsync(GroupMessage message)
    {
        try
        {
            await groupMessageStore.RecordAsync(message, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "群消息记录失败：群={GroupId}，消息ID={MessageId}", message.GroupId, message.MessageId);
        }
    }

    private void OnGroupMessageReceived(GroupMessage message)
    {
        var receivedAt = Stopwatch.GetTimestamp();
        if (agentConfig.AllowedGroups.Contains(message.GroupId) == false)
        {
            return;
        }

        // Microsoft.Data.Sqlite 的异步接口实际同步执行，将写入交给线程池，避免阻塞接收循环。
        // 记录与业务处理独立，原始正文不受后续前缀和艾特移除影响。
        _ = Task.Run(() => RecordGroupMessageAsync(message));
        if (message.SenderId == napCatConfig.BotAccount)
        {
            return;
        }

        if (agentConfig.BlacklistedGroups.Contains(message.GroupId))
        {
            return;
        }

        // 按原消息顺序取第一个文本段匹配指令，跳过前面的非文本段。
        var textContent = message.MessageContentList
            .OfType<TextContent>()
            .FirstOrDefault();
        var text = textContent?.Text.TrimStart() ?? "";
        var commandPrefix = agentConfig.IsTestMode ? "321" : "123";
        var hasPrefix = text.StartsWith(commandPrefix, StringComparison.Ordinal);
        var botAccount = napCatConfig.BotAccount.ToString(CultureInfo.InvariantCulture);
        var isBotMentioned = agentConfig.IsTestMode == false && message.MessageContentList
            .Any(content => content is AtContent atContent && atContent.Target == botAccount);
        if (hasPrefix == false && isBotMentioned == false)
        {
            return;
        }
        // 合法请求入队前移除触发前缀和机器人艾特，保留其他消息段及其顺序。
        if (hasPrefix)
        {
            textContent.Text = text[commandPrefix.Length..];
        }
        // 原始消息是否带艾特需在移除机器人艾特前记录，回复时决定是否仍需艾特。
        message.MessageContentList.RemoveAll(content => content is AtContent atContent && atContent.Target == botAccount);
        logger.LogInformation("收到群消息：群={GroupId}，发送者={SenderId}，消息段数={ContentCount}，开头文本={Text}",
            message.GroupId, message.SenderId, message.MessageContentList.Count, text);

        // 接收回调中不能等待消息处理完成，同一个接收循环还需要继续读取 API 响应。
        if (_messages.Writer.TryWrite((message, receivedAt)) == false)
        {
            logger.LogWarning("群消息队列已满或已关闭，跳过消息：群={GroupId}，发送者={SenderId}{NewLine}", message.GroupId, message.SenderId, Environment.NewLine);
            napCatClient.SendGroupMessageAsync(message.GroupId, message.SenderId, "群消息队列已满，请稍后再试", message.MessageId, message.NeedAtInReply, new CancellationToken());
        }
    }

    private async Task ProcessMessagesAsync(CancellationToken cancellationToken)
    {
        await foreach (var request in _messages.Reader.ReadAllAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var message = request.Message;
            try
            {
                logger.LogInformation("正在处理群消息：群={GroupId}，发送者={SenderId}", message.GroupId, message.SenderId);
                var messageId = await messageAgent.HandleMessageAsync(message, request.ReceivedTimestamp, cancellationToken);
                if (messageId is null)
                {
                    logger.LogInformation("群消息无需回复：群={GroupId}，发送者={SenderId}", message.GroupId, message.SenderId);
                    continue;
                }

                logger.LogInformation("群消息回复发送成功：群={GroupId}，消息ID={MessageId}", message.GroupId, messageId.Value);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "群消息处理或发送未完成：群={GroupId}，发送者={SenderId}", message.GroupId, message.SenderId);
            }
            finally
            {
                logger.LogInformation("群消息处理结束：群={GroupId}，发送者={SenderId}{NewLine}", message.GroupId, message.SenderId, Environment.NewLine);
            }
        }
    }
    #endregion
}
