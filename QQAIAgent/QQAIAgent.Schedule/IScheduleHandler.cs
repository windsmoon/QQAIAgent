namespace QQAIAgent.Schedule;

public interface IScheduleHandler
{
    #region properties
    /// <summary>定时任务实现的唯一名称，与任务配置中的 FunctionName 对应。</summary>
    string FunctionName { get; }
    #endregion

    #region methods
    /// <summary>执行一次任务。实现以单例注册，不同任务 Id 可能并发调用同一个实例。</summary>
    Task ExecuteAsync(ScheduleTask task, CancellationToken cancellationToken);
    #endregion
}
