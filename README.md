# QQAIAgent

基于 NapCat 的 QQ AI Agent，使用 C# 和 .NET 10 开发，目标运行环境为 Windows / Windows Server。设计目标是低内存占用、高度可配置和可扩展。

## 当前功能

通过 NapCat 的正向 WebSocket 接收群消息，并核验登录的机器人账号。白名单群中以 `123` 开头的消息会进入处理队列，例如 `123 你好`。

消息带有引用时，Agent 在处理阶段通过 NapCat 逐层获取被引用的群消息，按较早引用到当前请求的顺序交给模型。引用中的 `123` 不参与指令匹配，也不去掉引用中的前缀；引用链最多展开 `MaxReferenceDepth` 层，默认 3 层。当前模型接收文本，引用中的艾特转为文本，图片及尚未支持的消息段保留类型标记。获取引用失败时回复“获取引用消息失败，请稍后再试”，不继续请求模型。

`GroupMessageAgent` 去掉触发前缀，将配置的系统提示词放在消息列表最前面，再将当前问题作为一条用户消息传给 `IChatModel`。模型请求工具调用时，Agent 执行全部工具，将模型的完整工具调用消息和对应结果加入本次消息列表，再次请求模型，直到模型返回最终文本，发送到原群。系统提示词在本次请求的所有模型轮次中保留。调用工具时的中间文本不发送到 QQ。只有前缀或后接空白时，去掉前缀后的空字符串也会交给模型处理；模型结束调用而没有返回可用文本时，回复“回复内容为空”。

模型请求超时时，回复“模型请求超时，请稍后再试”；其他模型请求失败时，回复“模型请求失败，请稍后再试”。异常仍由宿主记录日志，后续消息继续处理。宿主退出导致的取消不发送失败提示。

调用日志记录每轮模型请求的开始、结束原因、工具调用数、回复字符数和耗时，以及每个工具的开始、完成、结果字符数和耗时。模型失败、工具失败、超时或跳过、空回复和次数上限使用 Warning 日志，请求取消使用 Information 日志。日志作用域携带群号、发送者、模型轮次及工具名称、调用 ID，便于追踪完整请求。控制台时间戳和作用域显示由 `QQAIAgent.Host/appsettings.json` 中的 `Logging.Console.FormatterOptions` 配置。

- 所有群共用一个容量为 100 的消息队列，按顺序完成每条消息的模型与工具调用和 QQ 回复。
- 模型实例作为单例复用，每条 QQ 请求单独维护消息列表，包含当前问题和本次调用的模型、工具消息，请求结束后不保存历史上下文。
- DeepSeek 在自己的项目中读取配置，并维护一个复用的 `HttpClient`。
- DeepSeek 工具调用、插件工具执行器和 Agent 循环（agent loop）已接入群消息入口，支持一次 QQ 请求内多轮调用模型和工具后回复。

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
| `agent.example.json` | `agent.local.json` | 群白名单、每条请求的模型调用次数上限、系统提示词 | Host |
| `deepseek.example.json` | `deepseek.local.json` | API Key、模型名称、完整请求地址、超时秒数 | DeepSeek 模型 |
| `tools.example.json` | `tools.local.json` | 工具定义、启用状态及各工具的超时秒数 | Host |
| `websearch.example.json` | `websearch.local.json` | 联网搜索 API Key、完整请求地址 | 联网搜索工具 |

`websearch.local.json` 使用腾讯云联网搜索的服务 API KEY 接入方式，`ApiKey` 填写联网搜索服务密钥，`Endpoint` 填写完整请求地址，模板为 `https://api.wsa.cloud.tencent.com/SearchPro`。密钥获取与接口说明见[腾讯云官方文档](https://cloud.tencent.com/document/product/1806/130615)。配置在每次调用联网搜索时读取，未调用搜索时不读取该文件。

`tools.local.json` 是工具配置数组，每个元素对应 `ChatToolDefinition`。Host 在启动时读取配置，将 `Enabled` 为 `true` 的工具定义列表注册为 `IReadOnlyList<ChatToolDefinition>`，供依赖注入获取。`Enabled` 省略时默认为 `true`。当前模板中获取时间（`get_time`）、联网搜索（`web_search`）和获取网页（`fetch_web_page`）均开启。`ParametersJson` 保存工具参数的 JSON Schema 字符串。工具定义的 `Name` 与插件实现的 `ITool.Name` 对应；开关控制开放给模型的工具定义，不影响插件加载，也不限制 QQ 指令直接使用基础功能。

每个工具对象的 `TimeoutSeconds` 控制该工具单次调用的超时秒数，填写正数，省略时默认为 30。当前配置中 `get_time` 为 5 秒、`web_search` 为 30 秒、`fetch_web_page` 为 60 秒。每次调用按对应工具的配置独立计时；超时后返回带原调用 ID 的工具失败消息，继续执行本轮剩余工具，再将结果交给模型处理。整个请求被取消时，仍然向上传递取消异常。该配置由工具执行器使用，不会发送给模型。

构建和发布 Host 时，配置文件会复制到宿主程序目录的 `Config/`。运行时以 `AppContext.BaseDirectory` 定位文件，不依赖启动时的工作目录。DeepSeek 在模型实例创建时读取自己的配置，配置对象不注册到 Host 的依赖注入容器中。

`agent.local.json` 示例：

```json
{
  "AllowedGroups": [123456789],
  "MaxModelCalls": 10,
  "MaxReferenceDepth": 3,
  "SystemPrompt": "你是 QQ 群聊中的聊天助手。用简短的简体中文纯文本直接回答，不使用 Markdown，默认用 1～3 句话，尽量控制在 150 字以内。"
}
```

`AllowedGroups` 填写允许处理的真实群号，空列表表示不处理任何群。`MaxModelCalls` 控制每条 QQ 请求最多调用模型的次数，省略时默认为 10，首次请求和生成最终回复的请求都计入。最后一次模型调用若仍请求工具，Agent 不再执行该轮工具，回复“已达到模型调用次数上限，请重新发起请求”。

`MaxReferenceDepth` 控制最多读取的引用层数，省略时默认为 3。直接引用计为第 1 层，不包含当前消息；设为 0 时不读取引用。达到上限或引用链结束后，使用已读取的引用上下文处理当前请求。

`SystemPrompt` 用于指导群聊回复格式和风格，省略或留空时不发送系统消息。模板中的默认提示词要求简体中文纯文本、自然直接、不使用 Markdown，通常用 1～3 句话并尽量控制在 150 字以内；搜索和网页内容只提炼关键信息，来源链接直接使用 URL。用户明确要求详细说明时可适当展开。格式和篇幅由提示词引导模型生成，程序不会按字数截断或删除 Markdown。该配置由 Host 在启动时读取，修改后需同步到宿主目录并重新启动。

## 本地运行

1. 准备 .NET 10 SDK，并在 NapCat 中配置正向 WebSocket 服务。
2. 在 Rider 中打开 `QQAIAgent/QQAIAgent.sln`，复制并填写本地配置。`napcat.local.json` 的地址、令牌和机器人账号应与 NapCat 配置对应。
3. 在 `agent.local.json` 中填写群白名单和模型调用次数上限，在 `tools.local.json` 中为每个工具填写 `TimeoutSeconds`，在 `deepseek.local.json` 中填写 API Key、模型名称、请求地址和超时秒数。使用联网搜索时，在 `websearch.local.json` 中填写腾讯云联网搜索服务 API Key 和完整请求地址；不使用时将 `web_search` 的 `Enabled` 设为 `false`。
4. 在 Rider 中运行 `QQAIAgent.Host`，保留运行前的 Build 步骤。Host 的项目引用会自动构建 DeepSeek 和内置工具插件；模型程序集复制到宿主输出目录，工具插件及其依赖复制到 `Plugins/QQAIAgent.Tools/`。
5. 在白名单群中发送 `123 你好` 测试文本回复，或发送 `123 请用 get_time 获取当前时间` 测试工具调用。模型请求获取时间时，Agent 执行 `GetTimeTool`，将结果交回模型生成最终回答，再回复该群。

修改源目录中的 JSON 后，需要将更新后的配置复制到宿主程序目录的 `Config/`；重新构建 Host 会按项目配置执行复制。NapCat、Agent 和工具定义配置在启动时读取，DeepSeek 配置在模型实例创建时读取，更新后需要重新启动宿主才能生效。联网搜索配置在每次调用时读取，更新宿主目录中的该文件后，下次搜索即可生效。

## 工具插件

`QQAIAgent.Abstractions` 定义 `ITool` 接口。插件通过 `Name` 标识工具，`ExecuteAsync(string argumentsJson, CancellationToken cancellationToken)` 接收参数 JSON，返回文本或 JSON 字符串。`QQAIAgent.Tools` 是内置插件项目，当前包含 `GetTimeTool`、`WebSearchTool` 和 `FetchWebPageTool`。`get_time` 返回服务器当前时间的 ISO 8601 字符串，包含时区偏移。

`web_search` 接收 `{"query":"搜索关键词或问题"}`，通过腾讯云 `SearchPro` 接口搜索公开网页。请求使用 `Authorization: Bearer API_KEY` 和 JSON 字段 `Query`。结果为包含 `query`、`results`、`requestId` 和 `message` 的 JSON，其中 `results` 将接口的 `Pages` JSON 字符串数组解码为对象数组，保留标题、摘要、来源地址、发布时间等原始字段；无结果时返回空数组，`message` 保留服务提示，未提供时为 `null`。HTTP 失败和腾讯云 `Response.Error` 交给现有工具执行器返回失败结果。工具复用一个 `HttpClient`，由宿主退出时释放，超时和取消沿用工具执行器的配置。

已有 `tools.local.json` 需将 `web_search` 的 `Enabled` 设为 `true`，并填写 `websearch.local.json`，重新启动 Host 后可在白名单群中发送 `123 请用 web_search 搜索今天的科技新闻`。

`fetch_web_page` 接收 `{"url":"https://example.com"}`，支持 HTTP/HTTPS、自动重定向、压缩响应以及 HTML 和纯文本内容。结果为包含 `url`（重定向后的最终地址）、`title` 和 `content` 的 JSON。HTML 使用 HtmlAgilityPack 解析，优先提取 `main`，没有时提取 `body`，过滤脚本、样式、注释和显式隐藏的内容，并保留段落换行。响应头未声明编码时读取 HTML 的 meta 编码，支持 UTF-8、GBK、GB2312 等编码及 BOM。工具复用一个 `HttpClient`，由宿主退出时释放，网络请求使用工具执行器传入的取消令牌和超时配置。

该工具获取服务器返回的静态正文，不执行 JavaScript；需要登录或浏览器渲染的内容可能无法获取。HTTP 失败和不支持的内容类型交由现有工具执行器返回失败结果。已有 `tools.local.json` 需将 `fetch_web_page` 的 `Enabled` 设为 `true`，保留其 `TimeoutSeconds` 配置，重新启动 Host 后可在群中发送 `123 请读取 https://example.com 并总结内容`。

Host 启动时扫描 `AppContext.BaseDirectory/Plugins/` 的直接子目录。每个插件目录以入口程序集名称命名，例如 `Plugins/QQAIAgent.Tools/QQAIAgent.Tools.dll`。没有同名入口 DLL 的目录会跳过。依赖 DLL、`.deps.json`、`.pdb` 和其他运行资源保留在该目录及其子目录中，不作为插件入口重复扫描。

每个入口程序集使用独立的 `AssemblyLoadContext`，通过 `AssemblyDependencyResolver` 解析托管和本机依赖。`QQAIAgent.Abstractions` 始终复用宿主加载的程序集，确保接口类型一致。一个插件 DLL 可以包含多个工具类；加载器会发现其中所有公开、非抽象且不含未绑定泛型参数的 `ITool` 实现，并分别注册为单例。Agent 或其他入口可以通过构造函数注入 `IEnumerable<ITool>` 获取全部实现，工具实例由依赖注入容器创建和管理。工具名称在所有插件之间应保持唯一，每次调用的临时状态应保留在方法内部。

开发期，Host 对内置插件的项目引用设置 `ReferenceOutputAssembly="false"`。该引用只建立构建依赖，Host 代码不能直接使用插件具体类。MSBuild 根据插件实际输出路径复制完整输出到宿主的插件目录，因此点运行时会自动构建并复制插件，Debug、Release 和目标框架随当前构建配置变化。发布 Host 时也会复制内置插件到发布目录。

添加第三方插件的步骤：

1. 创建目标框架为 `net10.0` 的类库，引用 `QQAIAgent.Abstractions`，添加一个或多个公开的 `ITool` 实现。
2. 在插件项目中设置 `<EnableDynamicLoading>true</EnableDynamicLoading>`。对接口项目的引用设置 `Private="false"` 和 `ExcludeAssets="runtime"`，接口程序集由 Host 提供。
3. 将插件完整构建输出放到宿主的 `Plugins/程序集名称/`，在 `tools.local.json` 中按每个工具的名称填写定义、开关和超时秒数，然后重新启动 Host。
4. 如果插件源码也在当前解决方案中，并希望运行 Host 时自动构建它，在 Host 中添加与内置插件相同的项目引用元数据：`ReferenceOutputAssembly="false"`、`Private="false"` 和 `OutputItemType="ToolPluginAssembly"`。现有复制任务会自动处理该插件输出。

`Infrastructure` 中的基础功能使用普通 C# 参数和返回值，工具实现负责适配模型参数和结果，QQ 指令处理逻辑也可以直接调用基础功能。插件发现、注册、模型工具调用响应解析和 Agent 的 `ToolExecutor` 已接入，群消息入口通过 Agent 循环完成工具执行与模型后续调用。

Host 将 `ToolExecutor` 注册为单例，执行器通过构造函数获取全部插件 `IEnumerable<ITool>` 和启用的工具定义。`ExecuteAsync(IReadOnlyList<ChatToolCall>, CancellationToken)` 按返回顺序逐个匹配工具名称，读取对应定义的 `TimeoutSeconds`，将参数 JSON 字符串直接交给对应的 `ITool.ExecuteAsync`，每次调用的结果整理为 `ChatRole.Tool` 消息，保留原调用 ID 到 `ToolCallId`。执行器只允许配置中已启用的工具；未启用、未配置、未加载、执行失败或超时的调用会返回错误结果消息，后续工具调用继续处理。请求取消会向上传递。每次执行的结果列表保留在方法内部，不共享请求状态。

执行器为每次工具调用创建关联请求取消和超时的令牌，并通过 `WaitAsync` 在取消时结束对工具任务的等待。插件应使用异步 API，并向内部操作传递收到的 `CancellationToken`；忽略令牌的任务可能在超时后继续运行，返回任务前的同步阻塞代码也无法由 `WaitAsync` 中断。

`GroupMessageAgent` 通过构造函数获取执行器，在每轮工具调用时执行：

```csharp
var toolMessages = await toolExecutor.ExecuteAsync(response.Message.ToolCalls, cancellationToken);
```

## 模型调用与部署

当前固定使用 `DeepSeekChatModel`。模型单例由依赖注入容器创建和管理，宿主退出时释放模型及其内部的 `HttpClient`。每条 QQ 请求首次通过 `ChatRequest.Messages` 传入当前问题，后续模型调用携带本次请求中累积的模型与工具消息。

`ChatRequest.Tools` 可以赋值。Agent 通过构造函数获取启用的工具定义，模型的 `SupportsToolCalling` 为 `true` 时才将其传入请求。DeepSeek 请求已支持工具定义、assistant 消息中的 `tool_calls` 和工具结果消息中的 `tool_call_id`，参数 Schema 会作为 JSON 对象发送；没有工具定义时省略 `tools` 字段。DeepSeek 的 `SupportsToolCalling` 已为 `true`，响应中的所有 `tool_calls` 会按顺序解析到 `ChatResponse.Message.ToolCalls`，保留调用 ID、工具名称和参数 JSON 字符串，工具调用消息的 `content` 可以为 `null`。参数字符串在响应解析时不会再次解析或改写，由工具执行阶段处理。

当前 QQ 请求会携带启用的工具定义。模型返回 `ChatFinishReason.ToolCalls` 时，群消息入口先保留完整的 assistant 消息，再调用 `ToolExecutor`，追加全部工具结果并继续请求模型；工具执行失败的结果也交给模型处理。模型结束工具调用后，仅发送最终文本到 QQ。循环受 `agent.local.json` 的 `MaxModelCalls` 限制，每一轮模型请求失败或超时都会发送对应失败提示。

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
    ├── tools.local.json
    └── websearch.local.json
```

以上仅列出主要文件，宿主的其他运行依赖仍需随发布输出一起部署。

## 使用协议

本项目采用自定义限制性许可，完整条款见 [LICENSE](LICENSE)：

- 仅供非商业学习、研究使用，禁止商业使用。
- 未经相应著作权人书面授权，不得二次开发。
- 在适用法律允许的范围内，未经授权使用造成的损失由使用者自行承担。
- 第三方组件遵守各自许可证，本项目协议不替代第三方许可证。
