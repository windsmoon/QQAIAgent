# QQAIAgent.Schedule

独立的内存任务调度类库，支持配置任务和动态单次任务。目标框架为 .NET 10，仅依赖日志抽象，不引用 Host、NapCat 或 Agent。

## 配置

示例位于 `../Config/schedule.example.json`。

- `Enabled`：全局开关，控制配置任务和动态任务。
- `TimeZone`：固定 UTC 偏移字符串，例如 `+8`、`-5`、`+5:30`、`-3:30`，不应用夏令时。
- `Tasks`：任务列表，每个任务由调用方绑定执行回调后注册。
- `Id`：大小写敏感的唯一标识；相同 Id 会替换原来的待执行计划。
- `FunctionName`：大小写敏感的任务实现名称，对应 `IScheduleHandler.FunctionName`，默认为 `send_group_message`。不同 Id 可以使用同一个实现。
- `ScheduleType`：`Once`、`Daily` 或 `Weekly`。
- `Time`：24 小时制 `HH:mm`，按全局 UTC 偏移解释。
- `Date`：仅 `Once` 使用，格式为 `yyyy-MM-dd`。
- `DaysOfWeek`：仅 `Weekly` 使用，例如 `["Monday", "Friday"]`；空列表表示没有执行日期。
- `GroupIds`：目标群号。仅 `[-1]` 表示 `AgentConfig.AllowedGroups` 中配置的所有群；空列表不发送，`-1` 与其他元素混合时不表示所有群。
- `ExcludedGroupIds`：排除的群号列表，默认为空。先确定目标群，再跳过其中被排除的群；指定群和所有群两种配置均适用。
- `Text`：消息内容，可省略，默认为空字符串。目标群、排除群和消息内容均透传给业务回调，调度器本身不发送消息。

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
        // 独立使用类库时可以直接绑定回调；Host 按 FunctionName 绑定处理器。
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

Host 启动时读取运行目录下的 `Config/schedule.local.json`，加载插件并以 `FunctionName` 为 key 建立处理器字典，NapCat 连接并核验账号成功后按配置绑定处理器、启动调度。启用的任务引用未加载的实现时启动失败；禁用的任务不查找实现。内置 `send_group_message` 向 `GroupIds` 中的群发送 `Text`，不引用消息、不艾特用户；消息内容为空时直接结束本次执行。其他处理器不受 `Text` 是否为空影响。单个群发送失败不会阻止其他目标群发送。

内置群消息处理器按目标群顺序发送。首个未被排除的群直接发送，后续每个目标群在上一次发送尝试结束后随机等待 3～10 秒（含两端）；被排除的群不增加等待，最后一群发送后不额外等待。等待期间响应取消。

配置使用 `TimeZone` 表示固定 UTC 偏移。修改本地配置后需重启程序；配置文件随 Host 构建和发布复制到运行目录。退出时只停止调度并通知取消，不等待已派发的任务回调结束。

## 外部定时任务插件

外部类库以 .NET 10 为目标框架，开启 `EnableDynamicLoading`，引用与宿主一致的 `QQAIAgent.Schedule`，并公开实现 `IScheduleHandler` 的非抽象、非开放泛型类。每个实现提供固定的 `FunctionName` 和 `ExecuteAsync`；业务依赖通过构造函数注入，不需要在 Host 中逐个注册外部类。

例如，以下处理器使用宿主提供的日志服务，执行时记录配置中的文本。插件项目还需引用与宿主一致的 `Microsoft.Extensions.Logging.Abstractions`：

```csharp
using Microsoft.Extensions.Logging;
using QQAIAgent.Schedule;

public sealed class LogScheduleHandler(ILogger<LogScheduleHandler> logger) : IScheduleHandler
{
    public string FunctionName => "log_schedule";

    public Task ExecuteAsync(ScheduleTask task, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("定时任务执行：任务={TaskId}，内容={Text}", task.Id, task.Text);
        return Task.CompletedTask;
    }
}
```

将插件输出及其依赖放到运行目录下，子目录名称必须与入口 DLL 名称一致：

```text
Plugins/
  MySchedules/
    MySchedules.dll
    MySchedules.deps.json
    其他依赖.dll
```

在 `Config/schedule.local.json` 的 `Tasks` 中添加对应计划，例如：

```json
{
  "Id": "monday-log",
  "FunctionName": "log_schedule",
  "Enabled": true,
  "ScheduleType": "Weekly",
  "DaysOfWeek": ["Monday"],
  "Time": "09:00",
  "Text": "本周任务开始"
}
```

- 统一加载器每个插件入口 DLL 只加载一次，同时发现 Tool 和定时任务实现；一个 DLL 可以包含多个实现，两类实现的名称空间互相独立。
- 处理器通过 DI 创建为单例，启动时构建 `FunctionName` 字典。重复名称（包括与内置处理器重名）会导致启动失败，不覆盖已注册实现。
- 加载上下文共享宿主的 `QQAIAgent.Abstractions`、`QQAIAgent.Schedule`、`QQAIAgent.Messaging` 和日志抽象程序集。插件可以注入宿主已注册的公共服务，例如 `IMessageSender`、`IGroupMessageQuery`、`ILogger<T>`；新增的插件私有业务依赖不会自动注册。
- 同一实例可能被不同 Id 的任务并发调用，每次执行的临时状态应保存在方法局部变量中，并响应传入的取消令牌。
- 插件与配置在启动时加载，更换 DLL 或修改配置后需要重启；不支持热加载。
- 直接使用 `ScheduleService.AddOrUpdate` 时仍可传入任意回调，`FunctionName` 只是任务数据，调度器本身不查找插件。
