# QQAIAgent

基于 NapCat 的 QQ AI Agent，使用 C# 和 .NET 10 开发，目标运行环境为 Windows / Windows Server。设计目标是低内存占用、高度可配置和可扩展。

## 当前功能

通过 NapCat 的正向 WebSocket 接收群消息，并核验登录的机器人账号。白名单群中以 `123` 开头的消息会进入处理队列，例如 `123 你好`。

`GroupMessageAgent` 去掉触发前缀，将当前问题作为一条用户消息传给 `IChatModel`，调用一次模型后把返回文本发送到原群。只有前缀而没有问题时，直接回复“输入内容为空”；模型没有返回可用文本时，回复“回复内容为空”。

模型请求超时时，回复“模型请求超时，请稍后再试”；其他模型请求失败时，回复“模型请求失败，请稍后再试”。异常仍由宿主记录日志，后续消息继续处理。宿主退出导致的取消不发送失败提示。

- 所有群共用一个容量为 100 的消息队列，按顺序完成模型请求和 QQ 回复。
- 模型实例作为单例复用，每条消息单独创建 `ChatRequest`，当前不保存历史上下文。
- DeepSeek 在自己的项目中读取配置，并维护一个复用的 `HttpClient`。
- DeepSeek 已支持工具调用请求和响应解析；群消息仍只调用一次模型，工具执行与 Agent 循环（agent loop）尚未接入。

## 目录结构

```text
QQAIAgent/
├── QQAIAgent/
│   ├── QQAIAgent.sln
│   ├── Config/                # 各模块的 JSON 配置与模板
│   ├── QQAIAgent.Host/        # 程序入口、依赖注入与消息队列
│   ├── QQAIAgent.Agent/       # 群消息处理与模型接口
│   ├── QQAIAgent.Abstractions/ # 插件与宿主共享的扩展接口
│   ├── QQAIAgent.Infrastructure/ # 可由工具和 QQ 指令复用的基础功能
│   ├── QQAIAgent.Tools/       # 通过 DLL 加载的内置工具插件
│   ├── QQAIAgent.Messaging/   # 群消息数据与发送接口
│   ├── QQAIAgent.NapCat/      # NapCat 协议适配
│   └── DeepSeek/             # 独立的大模型实现
├── Third/                    # 第三方程序的本地存放目录
├── LICENSE
└── README.md
```

Host 直接引用 DeepSeek 项目，并通过依赖注入将 `DeepSeekChatModel` 注册为 `IChatModel` 单例。`GroupMessageAgent` 通过构造函数获取模型并处理消息。DeepSeek 在自己的项目中读取配置和处理 HTTP 通信。

## 业务配置

业务 JSON 统一放在 `QQAIAgent/Config/`，与解决方案中的各项目文件夹同级。将模板复制为对应的 `*.local.json`，再填写实际配置；本地配置由 Git 忽略。

| 模板 | 本地配置 | 内容 | 读取方 |
| --- | --- | --- | --- |
| `napcat.example.json` | `napcat.local.json` | WebSocket 地址、访问令牌、机器人 QQ 账号 | Host |
| `agent.example.json` | `agent.local.json` | 群白名单 | Host |
| `deepseek.example.json` | `deepseek.local.json` | API Key、模型名称、完整请求地址、超时秒数 | DeepSeek 模型 |
| `tools.example.json` | `tools.local.json` | 工具定义及启用状态 | Host |

`tools.local.json` 是工具配置数组，每个元素对应 `ChatToolDefinition`。Host 在启动时读取配置，将 `Enabled` 为 `true` 的工具定义列表注册为 `IReadOnlyList<ChatToolDefinition>`，供依赖注入获取。`Enabled` 省略时默认为 `true`。当前配置中获取时间（`get_time`）开启，联网搜索（`web_search`）和获取网页（`fetch_web_page`）关闭。`ParametersJson` 保存工具参数的 JSON Schema 字符串。工具定义的 `Name` 与插件实现的 `ITool.Name` 对应；开关控制开放给模型的工具定义，不影响插件加载，也不限制 QQ 指令直接使用基础功能。

构建和发布 Host 时，配置文件会复制到宿主程序目录的 `Config/`。运行时以 `AppContext.BaseDirectory` 定位文件，不依赖启动时的工作目录。DeepSeek 在模型实例创建时读取自己的配置，配置对象不注册到 Host 的依赖注入容器中。

`agent.local.json` 示例：

```json
{
  "AllowedGroups": [123456789]
}
```

`AllowedGroups` 填写允许处理的真实群号，空列表表示不处理任何群。

## 本地运行

1. 准备 .NET 10 SDK，并在 NapCat 中配置正向 WebSocket 服务。
2. 在 Rider 中打开 `QQAIAgent/QQAIAgent.sln`，复制并填写四份本地配置。`napcat.local.json` 的地址、令牌和机器人账号应与 NapCat 配置对应。
3. 在 `agent.local.json` 中填写群白名单，在 `deepseek.local.json` 中填写 API Key、模型名称、请求地址和超时秒数。
4. 在 Rider 中运行 `QQAIAgent.Host`，保留运行前的 Build 步骤。Host 的项目引用会自动构建 DeepSeek 和内置工具插件；模型程序集复制到宿主输出目录，工具插件及其依赖复制到 `Plugins/QQAIAgent.Tools/`。
5. 在白名单群中发送 `123 你好`，由模型生成文本后回复到该群。

修改源目录中的 JSON 后，需要将更新后的配置复制到宿主程序目录的 `Config/`；重新构建 Host 会按项目配置执行复制。配置在启动或模型实例创建时读取，更新后需要重新启动宿主才能生效。

## 工具插件

`QQAIAgent.Abstractions` 定义 `ITool` 接口。插件通过 `Name` 标识工具，`ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)` 接收参数 JSON，返回文本或 JSON 字符串。`QQAIAgent.Tools` 是内置插件项目，当前包含 `GetTimeTool`，其名称为 `get_time`，返回服务器当前时间的 ISO 8601 字符串，包含时区偏移。

Host 启动时扫描 `AppContext.BaseDirectory/Plugins/` 的直接子目录。每个插件目录以入口程序集名称命名，例如 `Plugins/QQAIAgent.Tools/QQAIAgent.Tools.dll`。没有同名入口 DLL 的目录会跳过。依赖 DLL、`.deps.json`、`.pdb` 和其他运行资源保留在该目录及其子目录中，不作为插件入口重复扫描。

每个入口程序集使用独立的 `AssemblyLoadContext`，通过 `AssemblyDependencyResolver` 解析托管和本机依赖。`QQAIAgent.Abstractions` 始终复用宿主加载的程序集，确保接口类型一致。一个插件 DLL 可以包含多个工具类；加载器会发现其中所有公开、非抽象且不含未绑定泛型参数的 `ITool` 实现，并分别注册为单例。Agent 或其他入口可以通过构造函数注入 `IEnumerable<ITool>` 获取全部实现，工具实例由依赖注入容器创建和管理。工具名称在所有插件之间应保持唯一，每次调用的临时状态应保留在方法内部。

开发期，Host 对内置插件的项目引用设置 `ReferenceOutputAssembly="false"`。该引用只建立构建依赖，Host 代码不能直接使用插件具体类。MSBuild 根据插件实际输出路径复制完整输出到宿主的插件目录，因此点运行时会自动构建并复制插件，Debug、Release 和目标框架随当前构建配置变化。发布 Host 时也会复制内置插件到发布目录。

添加第三方插件的步骤：

1. 创建目标框架为 `net10.0` 的类库，引用 `QQAIAgent.Abstractions`，添加一个或多个公开的 `ITool` 实现。
2. 在插件项目中设置 `<EnableDynamicLoading>true</EnableDynamicLoading>`。对接口项目的引用设置 `Private="false"` 和 `ExcludeAssets="runtime"`，接口程序集由 Host 提供。
3. 将插件完整构建输出放到宿主的 `Plugins/程序集名称/`，在 `tools.local.json` 中按每个工具的名称填写定义和开关，然后重新启动 Host。
4. 如果插件源码也在当前解决方案中，并希望运行 Host 时自动构建它，在 Host 中添加与内置插件相同的项目引用元数据：`ReferenceOutputAssembly="false"`、`Private="false"` 和 `OutputItemType="ToolPluginAssembly"`。现有复制任务会自动处理该插件输出。

`Infrastructure` 中的基础功能使用普通 C# 参数和返回值，工具实现负责适配模型参数和结果，QQ 指令处理逻辑也可以直接调用基础功能。插件发现、注册和模型工具调用响应解析已经接入；Agent 工具执行和调用循环尚未接入，当前群消息仍只请求一次模型。

## 模型调用与部署

当前固定使用 `DeepSeekChatModel`。模型单例由依赖注入容器创建和管理，宿主退出时释放模型及其内部的 `HttpClient`。每次调用通过 `ChatRequest.Messages` 单独传入当前问题。

`ChatRequest.Tools` 可以赋值。Agent 通过构造函数获取启用的工具定义，模型的 `SupportsToolCalling` 为 `true` 时才将其传入请求。DeepSeek 请求已支持工具定义、assistant 消息中的 `tool_calls` 和工具结果消息中的 `tool_call_id`，参数 Schema 会作为 JSON 对象发送；没有工具定义时省略 `tools` 字段。DeepSeek 的 `SupportsToolCalling` 已为 `true`，响应中的所有 `tool_calls` 会按顺序解析到 `ChatResponse.Message.ToolCalls`，保留调用 ID、工具名称和参数 JSON 字符串，工具调用消息的 `content` 可以为 `null`。参数字符串在响应解析时不会再次解析或改写，由工具执行阶段处理。

当前 QQ 请求会携带启用的工具定义，但 Agent 尚未执行工具或继续请求模型。如果模型仅返回工具调用而没有文本，群消息处理会回复“回复内容为空”；完整工具问答需要继续接入 Agent 工具执行和调用循环。

发布时部署 Host 的完整发布输出，包括引用的程序集、其他运行依赖和实际使用的配置。主要文件如下：

```text
程序目录/
├── QQAIAgent.Host.exe
├── DeepSeek.dll
├── QQAIAgent.Agent.dll
├── QQAIAgent.Abstractions.dll
├── QQAIAgent.Infrastructure.dll
├── QQAIAgent.Messaging.dll
├── Plugins/
│   └── QQAIAgent.Tools/
│       ├── QQAIAgent.Tools.dll
│       ├── QQAIAgent.Tools.deps.json
│       └── QQAIAgent.Tools.pdb
└── Config/
    ├── napcat.local.json
    ├── agent.local.json
    ├── deepseek.local.json
    └── tools.local.json
```

以上仅列出主要文件，宿主的其他运行依赖仍需随发布输出一起部署。

## 使用协议

本项目采用自定义限制性许可，完整条款见 [LICENSE](LICENSE)：

- 仅供非商业学习、研究使用，禁止商业使用。
- 未经相应著作权人书面授权，不得二次开发。
- 在适用法律允许的范围内，未经授权使用造成的损失由使用者自行承担。
- 第三方组件遵守各自许可证，本项目协议不替代第三方许可证。
