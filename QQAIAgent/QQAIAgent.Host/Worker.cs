using System.Threading.Channels;
using QQAIAgent.Agent;
using QQAIAgent.Agent.Config;
using QQAIAgent.Messaging;
using QQAIAgent.NapCat;
using QQAIAgent.NapCat.Config;

namespace QQAIAgent.Host;

public class Worker(NapCatClient napCatClient, GroupMessageAgent messageAgent, AgentConfig agentConfig, NapCatConfig napCatConfig, ILogger<Worker> logger, IHostApplicationLifetime applicationLifetime) : BackgroundService
{
    #region fields
    private readonly Channel<GroupMessage> _messages = Channel.CreateBounded<GroupMessage>(new BoundedChannelOptions(100)
    {
        SingleReader = true,
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
                startupTimeout.CancelAfter(TimeSpan.FromSeconds(30));
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
                    throw new TimeoutException("NapCat 连接或账号核验超时（30 秒）。");
                }
            }

            processingTask = ProcessMessagesAsync(processingCancellation.Token);
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
            // Ctrl+C cancels connection or receive operations as part of normal shutdown.
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
                // Canceling the message processor and disconnect waiter is part of normal shutdown.
            }
            finally
            {
                await napCatClient.DisconnectAsync();
                logger.LogInformation("NapCat 后台服务已停止。");
            }
        }
    }

    private void OnGroupMessageReceived(GroupMessage message)
    {
        if (message.SenderId == napCatConfig.BotAccount)
        {
            return;
        }

        if (agentConfig.AllowedGroups.Contains(message.GroupId) == false)
        {
            return;
        }

        string text = message.Text.TrimStart();
        if (text.StartsWith("123") == false)
        {
            return;
        }
        logger.LogInformation("收到群消息：群={GroupId}，发送者={SenderId}，文本={Text}", message.GroupId, message.SenderId, message.Text);

        // Never wait in the receive callback: the same loop must keep reading API responses.
        if (_messages.Writer.TryWrite(message) == false)
        {
            logger.LogWarning("群消息队列已满或已关闭，跳过消息：群={GroupId}，发送者={SenderId}", message.GroupId, message.SenderId);
            napCatClient.SendGroupMessageAsync(message.GroupId, "群消息队列已满，请稍后再试", new CancellationToken());
        }
    }

    private async Task ProcessMessagesAsync(CancellationToken cancellationToken)
    {
        await foreach (var message in _messages.Reader.ReadAllAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                logger.LogInformation("正在处理群消息：群={GroupId}，发送者={SenderId}", message.GroupId, message.SenderId);
                var messageId = await messageAgent.HandleMessageAsync(message, cancellationToken);
                if (messageId is null)
                {
                    logger.LogInformation("群消息无需回复：群={GroupId}，发送者={SenderId}", message.GroupId, message.SenderId);
                    continue;
                }

                logger.LogInformation("测试指令已处理，pong 发送成功：群={GroupId}，消息ID={MessageId}", message.GroupId, messageId.Value);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "群消息处理或发送未完成：群={GroupId}，发送者={SenderId}", message.GroupId, message.SenderId);
            }
        }
    }
    #endregion
}
