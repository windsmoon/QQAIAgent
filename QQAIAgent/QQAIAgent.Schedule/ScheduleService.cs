using System.Globalization;
using Microsoft.Extensions.Logging;

namespace QQAIAgent.Schedule;

public class ScheduleService
{
    #region fields
    private readonly bool _enabled;
    private readonly TimeSpan _utcOffset;
    private readonly ILogger<ScheduleService> _logger;
    private readonly object _sync = new();
    private readonly Dictionary<string, ScheduledEntry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task> _executions = new(StringComparer.Ordinal);
    private bool _isRunning;
    #endregion

    #region constructors
    /// <summary>读取全局设置；配置中的任务由调用方绑定业务回调后逐个注册。</summary>
    public ScheduleService(ScheduleConfig config, ILogger<ScheduleService> logger)
    {
        _enabled = config.Enabled;
        _logger = logger;
        var offset = TimeSpan.ParseExact(config.TimeZone.TrimStart('+', '-'),
            ["%h", @"h\:mm", @"hh\:mm"], CultureInfo.InvariantCulture);
        _utcOffset = config.TimeZone.StartsWith('-') ? -offset : offset;
    }
    #endregion

    #region methods
    /// <summary>注册或替换配置任务。禁用或已过期的任务会移除同 Id 的待执行计划。</summary>
    public void AddOrUpdate(ScheduleTask task, Func<ScheduleTask, CancellationToken, Task> action)
    {
        if (_enabled == false || task.Enabled == false)
        {
            Remove(task.Id);
            return;
        }

        var snapshot = CopyTask(task);
        var nextExecution = GetNextExecution(snapshot, DateTimeOffset.UtcNow);
        lock (_sync)
        {
            if (nextExecution.HasValue)
            {
                _entries[snapshot.Id] = new ScheduledEntry(snapshot, nextExecution.Value, action);
            }
            else
            {
                _entries.Remove(snapshot.Id);
            }
        }
    }

    /// <summary>注册或替换动态单次任务，以 executeAt 为准，忽略传入模型的类型、日期和时间。</summary>
    public void AddOrUpdate(ScheduleTask task, DateTimeOffset executeAt, Func<ScheduleTask, CancellationToken, Task> action)
    {
        if (_enabled == false || task.Enabled == false || executeAt <= DateTimeOffset.UtcNow)
        {
            Remove(task.Id);
            return;
        }

        var snapshot = CopyTask(task);
        var localTime = executeAt.ToOffset(_utcOffset);
        snapshot.ScheduleType = ScheduleType.Once;
        snapshot.Date = localTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        snapshot.Time = localTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        lock (_sync)
        {
            // 实际执行时刻保留传入的秒和更小精度，展示字段仍遵循配置的 HH:mm 格式。
            _entries[snapshot.Id] = new ScheduledEntry(snapshot, executeAt.ToUniversalTime(), action);
        }
    }

    /// <summary>移除待执行计划，不取消已经领取执行的回调。</summary>
    public bool Remove(string taskId)
    {
        lock (_sync)
        {
            return _entries.Remove(taskId);
        }
    }

    /// <summary>运行唯一调度循环。退出时通知回调取消，不等待回调结束。</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (_enabled == false)
        {
            return;
        }

        lock (_sync)
        {
            if (_isRunning)
            {
                throw new InvalidOperationException("同一个调度器不能同时运行多个调度循环。");
            }

            _isRunning = true;
        }

        using var executionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // 回调可能在调度循环退出后才开始，提前保存令牌，避免访问已释放的取消源。
        var executionToken = executionCancellation.Token;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
            do
            {
                executionToken.ThrowIfCancellationRequested();
                lock (_sync)
                {
                    var now = DateTimeOffset.UtcNow;
                    foreach (var (id, entry) in _entries)
                    {
                        // 还没到时间或者同一个 id 已经有在执行了
                        if (entry.NextExecution > now || _executions.ContainsKey(id))
                        {
                            continue;
                        }

                        // 领取和登记在同一把锁内完成；同步耗时回调也不会阻塞调度循环。
                        _executions[id] = Task.Run(() => ExecuteAsync(id, entry, executionToken));
                    }
                }
            }
            while (await timer.WaitForNextTickAsync(executionToken));
        }
        catch (OperationCanceledException) when (executionCancellation.IsCancellationRequested)
        {
            // 停止调度属于正常退出。
        }
        finally
        {
            try
            {
                executionCancellation.Cancel();
            }
            finally
            {
                lock (_sync)
                {
                    _isRunning = false;
                }
            }
        }
    }

    private async Task ExecuteAsync(string id, ScheduledEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 回调拿到独立副本，修改业务参数不会改变已经注册的周期计划。
            // TODO GC
            await entry.Action(CopyTask(entry.Task), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 宿主停止时不重试本次执行。
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "定时任务执行未完成：任务={TaskId}", id);
        }
        finally
        {
            lock (_sync)
            {
                try
                {
                    // 执行期间同 Id 可以更新或移除，旧回调结束时不能覆盖新的计划。
                    // ReferenceEquals 是否在执行期间，这个任务被更新了
                    if (_entries.TryGetValue(id, out var current) && ReferenceEquals(current, entry))
                    {
                        var now = DateTimeOffset.UtcNow;
                        var nextExecution = entry.Task.ScheduleType == ScheduleType.Once
                            ? null
                            : GetNextExecution(entry.Task, now);
                        if (nextExecution.HasValue)
                        {
                            entry.NextExecution = nextExecution.Value;
                        }
                        else
                        {
                            _entries.Remove(id);
                        }
                    }
                }
                finally
                {
                    _executions.Remove(id);
                }
            }
        }
    }

    private DateTimeOffset? GetNextExecution(ScheduleTask task, DateTimeOffset after)
    {
        var time = TimeOnly.ParseExact(task.Time, "HH:mm", CultureInfo.InvariantCulture);
        var localAfter = after.ToOffset(_utcOffset);
        if (task.ScheduleType == ScheduleType.Once)
        {
            var date = DateOnly.ParseExact(task.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var execution = new DateTimeOffset(date.ToDateTime(time), _utcOffset).ToUniversalTime();
            return execution > after ? execution : null;
        }

        if (task.ScheduleType != ScheduleType.Weekly)
        {
            return null;
        }
        var today = DateOnly.FromDateTime(localAfter.DateTime);
        // 包含今天及下周同一天，覆盖每周只配置一天且今天的时间已过的情况。
        for (var day = 0; day <= 7; day++)
        {
            var date = today.AddDays(day);
            if (task.DaysOfWeek.Contains(date.DayOfWeek) == false)
            {
                continue;
            }

            var execution = new DateTimeOffset(date.ToDateTime(time), _utcOffset).ToUniversalTime();
            if (execution > after)
            {
                return execution;
            }
        }

        return null;
    }

    private static ScheduleTask CopyTask(ScheduleTask task)
    {
        return new ScheduleTask
        {
            Id = task.Id,
            FunctionName = task.FunctionName,
            Enabled = task.Enabled,
            GroupIds = [.. task.GroupIds],
            ExcludedGroupIds = [.. task.ExcludedGroupIds],
            ScheduleType = task.ScheduleType,
            Time = task.Time,
            Date = task.Date,
            DaysOfWeek = [.. task.DaysOfWeek],
            Text = task.Text
        };
    }
    #endregion

    #region nested types
    private sealed class ScheduledEntry(ScheduleTask task, DateTimeOffset nextExecution, Func<ScheduleTask, CancellationToken, Task> action)
    {
        public ScheduleTask Task { get; } = task;
        public DateTimeOffset NextExecution { get; set; } = nextExecution;
        public Func<ScheduleTask, CancellationToken, Task> Action { get; } = action;
    }
    #endregion
}
