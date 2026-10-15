# QQAIAgent.Schedule

独立的内存任务调度类库，支持配置任务和动态单次任务。目标框架为 .NET 10，仅依赖日志抽象，不引用 Host、NapCat 或 Agent。

## 配置

示例位于 `../Config/schedule.example.json`。

- `Enabled`：全局开关，控制配置任务和动态任务。
- `TimeZoneId`：固定 UTC 偏移字符串，例如 `+8`、`-5`、`+5:30`、`-3:30`，不应用夏令时。
- `Tasks`：任务列表，每个任务由调用方绑定执行回调后注册。
- `Id`：大小写敏感的唯一标识；相同 Id 会替换原来的待执行计划。
- `ScheduleType`：`Once`、`Daily` 或 `Weekly`。
- `Time`：24 小时制 `HH:mm`，按全局 UTC 偏移解释。
- `Date`：仅 `Once` 使用，格式为 `yyyy-MM-dd`。
- `DaysOfWeek`：仅 `Weekly` 使用，例如 `["Monday", "Friday"]`；空列表表示没有执行日期。
- `GroupIds`、`Text`：透传给业务回调的参数，调度器不发送消息；`Text` 可省略，默认为空字符串。

读取配置时注册 `JsonStringEnumConverter`，以支持 `DaysOfWeek` 中的星期名称：

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using QQAIAgent.Schedule;

static async Task RunScheduleAsync(
    string configPath,
    ILogger<ScheduleService> logger,
    CancellationToken cancellationToken)
{
    var options = new JsonSerializerOptions();
    options.Converters.Add(new JsonStringEnumConverter());
    var json = await File.ReadAllTextAsync(configPath, cancellationToken);
    var config = JsonSerializer.Deserialize<ScheduleConfig>(json, options);
    var scheduler = new ScheduleService(config, logger);

    foreach (var task in config.Tasks)
    {
        // 实际接入时可根据 task.Id 选择业务方法，或绑定统一的消息发送方法。
        scheduler.AddOrUpdate(task, (current, token) =>
        {
            token.ThrowIfCancellationRequested();
            logger.LogInformation("执行任务：{TaskId}，内容：{Text}", current.Id, current.Text);
            return Task.CompletedTask;
        });
    }

    await scheduler.RunAsync(cancellationToken);
}
```

全局设置在构造服务时读取。修改原配置对象不会热更新运行中的服务；修改任务后应重新调用 `AddOrUpdate`。注册与执行回调都使用任务数据的副本，调用方修改列表或业务参数不会改变已注册计划。

## 动态任务

以下代码可在调度循环运行期间调用。`executeAt` 使用 `DateTimeOffset`，直接确定执行时刻，不受服务器本地时区影响。

```csharp
scheduler.AddOrUpdate(
    new ScheduleTask
    {
        Id = "temporary-reminder",
        GroupIds = [123456789],
        Text = "这是一条动态提醒。"
    },
    DateTimeOffset.UtcNow.AddMinutes(10),
    async (task, token) =>
    {
        await SendReminderAsync(task, token);
    });

// 再次使用同一 Id 注册可以更新时间；删除只影响待执行计划。
scheduler.Remove("temporary-reminder");
```

`SendReminderAsync` 由业务方实现。动态重载始终注册单次任务，忽略传入的 `ScheduleType`、`Date` 和 `Time`；回调收到的类型为 `Once`，日期和时间按全局 UTC 偏移填写。内部执行时间保留传入的秒和更小精度，回调的 `Time` 字段仍为 `HH:mm`。

## 执行约定

- 调用方启动并持有 `RunAsync` 返回的任务。同一个服务同时只允许一个调度循环。
- 调度循环每秒检查一次到期任务，不保证精确到秒；线程调度或负载可能导致延迟。
- 不同 Id 的回调可并发执行，同一 Id 不会重入。领取执行后，更新或移除不会撤销该次回调。
- 执行期间更新同一 Id 时，新计划等待旧回调结束。旧回调结束不会覆盖或删除新计划；新计划已经到期时会在后续检查中执行。
- 单次任务结束后移除；周期任务结束后计算下一个未来时间，执行期间错过的轮次不补跑。普通轮询延迟会执行一次到期任务，不累计补跑。
- 注册时已过期的单次任务会被跳过，并移除同 Id 原有的待执行计划。每日和每周任务从注册时刻之后的下一次开始。
- 回调抛出异常时记录警告，不自动重试；其他任务继续调度，周期任务仍安排下一次。
- 取消 `RunAsync` 的令牌后停止领取任务，将取消通知传给回调，直接结束调度循环，不等待回调完成。正在执行的回调自行响应取消。
- 全局禁用时不注册任务，`RunAsync` 直接完成；单个任务禁用后重新注册会移除其待执行计划。
- 不持久化任务和执行记录。进程重启后动态任务丢失，配置任务需要重新加载并注册。

本类库尚未接入 Host 启动流程，配置示例不会自动生效。
