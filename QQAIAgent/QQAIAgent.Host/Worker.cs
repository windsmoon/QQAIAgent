using QQAIAgent.NapCat;

namespace QQAIAgent.Host;

public class Worker(NapCatClient napCatClient, ILogger<Worker> logger, IHostApplicationLifetime applicationLifetime) : BackgroundService
{
    #region fields
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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

            await napCatClient.WaitForDisconnectAsync(stoppingToken);
            if (stoppingToken.IsCancellationRequested == false)
            {
                logger.LogWarning("NapCat 已关闭连接，正在停止宿主。");
                applicationLifetime.StopApplication();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Ctrl+C 会取消连接或接收操作，属于正常停止。
        }
        finally
        {
            await napCatClient.DisconnectAsync();
            logger.LogInformation("NapCat 后台服务已停止。");
        }
    }
    #endregion
}
