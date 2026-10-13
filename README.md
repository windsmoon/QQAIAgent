# QQAIAgent

基于 NapCat 的 QQ 群 AI Agent，使用 C# 和 .NET 10 开发，面向 Windows / Windows Server，支持文本、图片与视频输入、多轮工具调用、引用消息和合并转发。

## 安装与部署

1. 安装并登录 NapCat，开启正向 WebSocket 服务，记下地址、访问令牌和机器人 QQ 号。
2. 从源码运行时安装 .NET 10 SDK，用 Rider 打开 `QQAIAgent/QQAIAgent.sln`。部署依赖框架的发布版本时，服务器需安装 .NET 10 运行时。
3. 按下节说明，将 `QQAIAgent/Config/` 中的 example 文件复制为 local 文件并填写配置。
4. 本地运行 `QQAIAgent.Host`；服务器部署该项目的完整发布输出，再在程序目录运行 `QQAIAgent.Host.exe`。构建与发布会复制配置和内置工具插件，部署时保留 `Config/`、`Plugins/`、`appsettings.json` 及全部运行依赖。

启动后会连接 NapCat 并核验机器人账号。普通模式在允许的群内发送 `123 你好`，测试模式发送 `321 你好`。业务配置固定从程序目录的 `Config/` 读取，不依赖启动时的工作目录。

## 需要配置哪些文件

example 文件位于 [QQAIAgent/Config](QQAIAgent/Config/)。运行时读取 local 文件，修改 example 不会直接改变正在运行的程序。本地 JSON 配置和系统提示词由 Git 忽略。

| example 文件 | 复制后的文件 | 必填或需要确认的内容 |
| --- | --- | --- |
| [napcat.example.json](QQAIAgent/Config/napcat.example.json) | `napcat.local.json` | `WebSocketUrl`、`BotAccount`；`AccessToken` 与 NapCat 的设置一致 |
| [agent.example.json](QQAIAgent/Config/agent.example.json) | `agent.local.json` | 填写 `AllowedGroups`，确认 `IsTestMode`；其余限制可先沿用模板 |
| [deepseek.example.json](QQAIAgent/Config/deepseek.example.json) | `deepseek.local.json` | 填写 `ApiKey`，确认模型 `Model`、完整请求地址 `Endpoint` 和超时 |
| [tools.example.json](QQAIAgent/Config/tools.example.json) | `tools.local.json` | 按需设置 `Enabled` 和 `TimeoutSeconds`；保留工具名称和参数定义 |
| [system-prompt.example.md](QQAIAgent/Config/system-prompt.example.md) | `system-prompt.local.md` | 按用途修改提示词中的称呼、QQ 信息和回复要求；允许内容为空白，文件必须存在 |
| [websearch.example.json](QQAIAgent/Config/websearch.example.json) | `websearch.local.json` | 启用 `web_search` 时填写服务 `ApiKey`，确认 `Endpoint`；未使用搜索时不读取此文件 |
| [videounderstanding.example.json](QQAIAgent/Config/videounderstanding.example.json) | `videounderstanding.local.json` | 启用 `understand_video` 时填写百炼 `ApiKey`，确认模型、输出上限、视频输入限制和 `FfprobePath`；未使用视频理解时不读取此文件 |

前五个 local 文件需要准备。模板默认启用获取时间和调整思考强度，关闭联网搜索、网页读取与视频理解；启用搜索或视频理解时再准备对应配置。

### Agent 配置

`agent.local.json` 示例，群号需替换为实际允许的群号：

```json
{
  "AllowedGroups": [123456789],
  "BlacklistedGroups": [],
  "IsTestMode": false,
  "MaxModelCalls": 20,
  "QQRequestTimeoutSeconds": 300,
  "MaxReferenceDepth": 5,
  "MaxForwardDepth": 5,
  "MaxForwardMessagesPerLayer": 20
}
```

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `AllowedGroups` | `[]` | 允许记录和处理的群；空列表不处理任何群 |
| `BlacklistedGroups` | `[]` | 在允许列表中的黑名单群只记录消息，不触发模型 |
| `IsTestMode` | `false` | 普通模式使用 `123` 或 @ 机器人触发；测试模式仅接受 `321` 前缀 |
| `MaxModelCalls` | `20` | 单条请求的模型调用总次数，包含首次请求和最终回复请求 |
| `QQRequestTimeoutSeconds` | `300` | 从接收消息到正常回复的总时限，包含排队、引用、转发、图片处理、模型和工具调用 |
| `MaxReferenceDepth` | `5` | 最大引用层数，直接引用计为第 1 层；`0` 不读取引用 |
| `MaxForwardDepth` | `5` | 合并转发的展开深度限制，计数边界见下文 |
| `MaxForwardMessagesPerLayer` | `20` | 每个合并转发取前多少条节点消息；超出时给出截断说明 |

超时字段和模型调用次数填写正数。达到最后一次模型调用且模型仍要求工具时，不执行该轮工具，回复“已达到模型调用次数上限，请重新发起请求”。

### NapCat 与模型配置

`napcat.local.json` 的 `WebSocketUrl` 填写正向 WebSocket 地址，`BotAccount` 填写实际登录账号；启动时会通过 `get_login_info` 核验，账号不一致会导致启动失败。

| 配置文件 | 超时字段 | 省略时的默认值 | 当前 example 值 |
| --- | --- | --- | --- |
| `napcat.local.json` | `StartupTimeoutSeconds` | 30 秒 | 30 秒 |
| `napcat.local.json` | `ApiTimeoutSeconds` | 30 秒 | 30 秒 |
| `napcat.local.json` | `DisconnectTimeoutSeconds` | 5 秒 | 5 秒 |
| `deepseek.local.json` | `TimeoutSeconds` | 60 秒 | 120 秒 |

NapCat 启动超时覆盖连接与账号核验；接口超时覆盖等待接口锁、发送与接收响应；断开超时覆盖关闭连接的过程。

当前模型适配器为 `DeepSeekChatModel`，example 使用 `deepseek-flash` 和 `https://api.deepseek.com/chat/completions`。`Endpoint` 必须是完整 HTTPS 地址，API Key 和模型名称不能为空。模型配置在实例创建时读取，模型和内部 `HttpClient` 作为单例复用。

DeepSeek 的超时分别用于每次聊天 HTTP 请求，以及每张图片的下载与上传。图片下载和上传共用一次时限，多张图片准备与聊天请求仍受 QQ 请求总时限约束。

### 工具与系统提示词

`tools.local.json` 是工具定义数组。每项包含 `Name`、`Description`、`ParametersJson`、`Enabled` 和 `TimeoutSeconds`。`ParametersJson` 是 JSON Schema 字符串，`Name` 必须对应已加载插件的 `ITool.Name`。省略 `Enabled` 时为 `true`，省略工具超时时为 30 秒。

| 工具 | 参数 | example 启用状态 | example 超时 | 功能 |
| --- | --- | --- | --- | --- |
| `get_time` | `{}` | 开启 | 5 秒 | 返回服务器当前时间的 ISO 8601 字符串，包含时区偏移 |
| `set_reasoning_effort` | `{"effort":"high"}` | 开启 | 5 秒 | 调整本条请求下一轮模型调用的思考强度 |
| `web_search` | `{"query":"搜索关键词"}` | 关闭 | 30 秒 | 通过腾讯云 SearchPro 搜索，返回查询词、结果、请求 ID 和服务提示 |
| `fetch_web_page` | `{"url":"https://example.com"}` | 关闭 | 60 秒 | 读取 HTML 或纯文本，返回最终地址、标题和正文 |
| `understand_video` | `{"url":"https://example.com/video.mp4","prompt":"总结视频中的关键事件"}` | 关闭 | 180 秒 | 通过阿里云百炼分析视频画面和声音，返回分析内容、模型、完成原因和 Token 用量 |

联网搜索使用服务 API KEY 的 Bearer 鉴权，example 已填写完整地址 `https://api.wsa.cloud.tencent.com/SearchPro`，仍需填写自己的密钥。接入方式见[腾讯云官方文档](https://cloud.tencent.com/document/product/1806/130615)。网页读取支持重定向、压缩响应及编码识别，提取静态正文，不执行 JavaScript；需要登录或浏览器渲染的网页可能无法获取。

视频理解使用百炼 OpenAI 兼容接口和 Bearer 鉴权，`Endpoint` 填完整的 `chat/completions` 地址，并使用对应地域的 API Key。默认模型为 `qwen3.8-omni-flash`，同时理解视频画面和声音，返回文本。`Fps` 为视频采样帧率，默认 `2`；`ReasoningEffort` 示例使用 `low`，可设为 `none`（关闭思考）、`minimal`、`medium`、`high`、`xhigh`、`max`；`MaxOutputTokens` 限制单次输出，默认 `4096`。视频 URL 直接交给百炼处理，客户端不缓存视频、不生成视频 Base64；必须提供百炼可直接访问的视频文件地址，普通视频网页链接不能作为输入。接入方式见[阿里云百炼官方文档](https://help.aliyun.com/zh/model-studio/qwen-omni)。QQ 视频消息段已接入：启用 `understand_video` 并填写百炼配置后，主模型可根据用户问题调用工具分析正文、引用和合并转发中的视频。

工具在请求百炼前使用 ffprobe 读取视频的实际大小与时长，不依赖 NapCat 是否提供时长。`MaxVideoSizeMB` 默认 `100`（每 MB 按 1024 × 1024 字节计算），`MaxVideoDurationSeconds` 默认 `180`；超过任一上限时提示裁剪，无法确定大小或时长时返回说明，均不调用百炼。探测与模型调用共用工具超时和 QQ 请求总时限，取消时会终止 ffprobe 子进程。

ffprobe 随项目放在 [Third/FFmpeg](Third/FFmpeg/README.md)，Host 构建和发布时复制到程序目录的 `Tools/FFmpeg/`。`FfprobePath` 默认值为 `Tools/FFmpeg/ffprobe.exe`；可改为其他可执行文件的绝对路径，带目录的相对路径以程序目录为基准。无需在部署机器另外安装 FFmpeg。

每条新请求从 `minimal` 思考强度开始。工具支持 `minimal`、`low`、`medium`、`high`、`max`；未调整时沿用本条请求上一轮的强度，同轮多次有效设置以最后一次为准。当前 DeepSeek 映射为：`minimal` 关闭思考，`low` 使用 low，`medium/high` 使用 high，`max` 使用 max。不同请求的工具上下文互不共享。

系统提示词全文作为系统消息发送，不渲染 Markdown。模板要求简体中文、简短自然的纯文本回复，允许按需求展开；格式由提示词引导，程序不按字数截断或删除 Markdown。

### 配置更新何时生效

| 修改位置 | 生效方式 |
| --- | --- |
| 源目录 `QQAIAgent/Config/` | 将 local 文件同步到宿主的 `Config/`；构建或发布也会复制 |
| 宿主中的 NapCat、Agent、工具定义、系统提示词、DeepSeek 配置 | 重启宿主 |
| 宿主中的 `websearch.local.json` | 下次搜索读取，无需重启 |
| 宿主中的 `videounderstanding.local.json` | 下次视频理解读取，无需重启 |
| `appsettings.json` 中的数据库和文件日志路径 | 重启宿主后使用新路径 |

## 消息触发与内容处理

当前接收群消息事件及机器人自身群消息上报，支持消息段数组和 CQ 字符串；私聊不进入该处理入口。只有 `AllowedGroups` 中的群会记录消息；机器人自身消息和黑名单群消息记录后不进入业务队列。

前缀判断按原顺序寻找**第一个非空白文本段**，跳过空字符串、纯空白文本以及前面的 @、图片等非文本段，再去掉该文本的前导空白。普通模式判断是否以 `123` 开头，测试模式判断 `321`；前缀后不要求空格，例如 `123你好` 也能触发。

| 当前消息 | 普通模式 | 测试模式 |
| --- | --- | --- |
| `123 你好` | 触发 | 不触发 |
| `321 你好` | 不触发 | 触发 |
| @ 机器人，再输入普通问题 | 触发 | 不触发 |
| 引用他人图片，附带 @ 他人和空白文本段，再输入 `123 看看这张图` | 触发 | 不触发 |
| 引用他人图片，附带 @ 他人和空白文本段，再输入 `321 看看这张图` | 不触发 | 触发 |

只判断第一个非空白文本段，不继续搜索后续有效文本段中的前缀。@ 他人、@ 全体成员、引用内部的 @ 或前缀不会单独触发；普通模式单独 @ 机器人可以触发。合法请求入队前移除匹配到的前缀和当前正文中的机器人 @，其他正文内容保留。只发送前缀时，移除后为空的文本也会交给模型。

| 消息类型 | 交给模型的内容 |
| --- | --- |
| 文本 | 正文文本，当前请求移除已匹配的触发前缀 |
| 图片、商城表情 `mface` | 图片输入，保留在内容中的原顺序 |
| 视频 `video` | 带视频地址的文字标记，提示主模型在需要理解内容时调用 `understand_video`；缺少 HTTP/HTTPS 地址时保留无法分析的说明 |
| @ 他人 / 全体成员 | `@QQ号` / `@全体成员` 文本 |
| QQ 表情 `face` | `[表情:名称]`；未知名称使用 `[表情]` |
| JSON 卡片 | 按已解析到的摘要、标题、描述和链接生成文字；没有可提取字段时显示通用卡片标记 |
| 合并转发 `forward` | 展开为发送者标签和各节点正文，图片继续作为图片输入 |
| 其他未支持类型 | 当前正文跳过；引用正文保留未支持类型的文字标记 |

### 引用、转发与图片

引用消息通过 NapCat `get_msg` 逐层读取。模型消息顺序为系统提示词、较早引用、较近引用、当前请求；每层引用带发送者 QQ 和上下文说明。引用中的前缀不匹配也不移除。达到 `MaxReferenceDepth` 后使用已获取的内容；获取引用失败时回复“获取引用消息失败，请稍后再试”，不再请求模型。

合并转发通过 `get_forward_msg` 展开，节点标注群名片或昵称及发送者 QQ，并受深度与节点数限制。获取失败时以“合并转发消息，获取失败”的标记继续处理；更深层未展开内容使用省略标记。转发资源按 ID 缓存，命中后直接使用本地数据。

视频消息支持消息段数组与 CQ 字符串，共用正文、引用及转发节点的解析与转换流程。解析保留 `file`、`url` 和 `file_size`；优先传入 `url`，未提供时使用 `file` 中的 HTTP/HTTPS 地址。仅提供本地路径或文件标识时，当前不能交给百炼分析。

当前转发深度的计数起点有差异：正文中的首层为 1，引用中的首层为 0。因此 `MaxForwardDepth = 0` 时，正文不展开转发，引用中的首层仍会展开；同一配置下引用中的转发可多展开一层。

图片使用 NapCat 提供的 URL，由模型适配器流式下载并上传到与聊天接口同路径前缀的 `files` 接口，再使用 `file_id` 发送给模型。不落盘保存图片，也不生成整张图片的 Base64 字符串。上传请求设置 30 天过期，本地文件 ID 按 29 天有效期缓存；上传或读取图片失败会进入模型请求失败流程。此链路要求图片 URL 可访问，并且配置的服务支持所使用的文件输入协议。

## 请求执行与运行数据

所有群共用容量为 **50** 的待处理队列。消费者数量为允许群数量，至少保留 1 个消费者；多个消费者并行处理请求，同一群的消息也可能并发，回复顺序不保证与发送顺序相同。队列满时跳过当前请求并尝试回复“群消息队列已满，请稍后再试”。

每条请求单独维护模型消息列表和 `ToolContext`，保留本次引用、正文、模型消息和工具结果，结束后不作为后续请求的历史上下文。模型请求工具时，按顺序执行本轮工具，将结果按调用 ID 回传并继续请求模型；工具调用期间的中间文本不发送到 QQ，只发送最终文本。当前回复会 @ 提问者，并在有消息 ID 时引用提问消息。

单个工具失败、超时、未启用或未加载时，生成工具错误结果交给模型继续处理；整个请求取消时向上传递取消。插件应传递并响应 `CancellationToken`，忽略取消的任务可能在执行器结束等待后继续运行。

| 情况 | 处理结果 |
| --- | --- |
| 请求总时限到达 | 取消处理，回复“请求处理超时，请稍后再试” |
| 单次模型请求超时 | 回复“模型请求超时，请稍后再试” |
| 其他模型请求失败 | 回复“模型请求失败，请稍后再试” |
| 模型最终文本为空白 | 回复“回复内容为空” |
| 模型调用次数耗尽 | 回复“已达到模型调用次数上限，请重新发起请求” |
| 宿主退出 | 取消当前处理，不发送取消提示 |
| NapCat 连接结束 | 停止宿主；当前没有自动重连流程 |

排队已耗尽总时限的请求，出队后直接进入超时流程。总时限覆盖正常回复，超时提示使用宿主令牌发送，并受 NapCat 单次接口超时约束。

### 日志、统计与缓存

路径由宿主 [appsettings.json](QQAIAgent/QQAIAgent.Host/appsettings.json) 配置，相对路径以程序目录为基准，启动时自动创建所需目录和表。

| 路径 | 配置项 | 保存内容 |
| --- | --- | --- |
| `Log/qqaiaagent.log` | `Logging.File.Path` | 追加式 UTF-8 文件日志，包含请求、模型轮次和工具作用域 |
| `Data/qqaiaagent.sqlite3` | `Database.Path` | 请求、模型和工具统计，以及 `image_files` 图片文件 ID 缓存 |
| `Data/Messages/<群号>.sqlite3` | `Database.MessagesDirectory` | 允许群的原始消息记录 |
| `Data/Messages/forward.sqlite3` | `Database.MessagesDirectory` | `forward_messages` 合并转发响应缓存 |

请求统计使用 `requests`、`model_calls`、`tool_calls` 关联机器人、群、用户、轮次、思考强度、时间、耗时、状态和回复消息 ID。实际开始执行的工具调用才计入次数，未启用、未加载及次数上限后未执行的工具不计入。统计表不保存用户或模型正文、工具参数、工具结果和异常正文。

每轮记录接口返回的输入、输出、总 Token、缓存命中/未命中 Token 和可选思考 Token。未返回的字段保存为 `NULL`，零值保存为 `0`；总量使用接口返回值，不额外累加思考 Token。字段说明见 [DeepSeek 官方用量文档](https://api-docs.deepseek.com/api/create-chat-completion/)。`request_statistics` 汇总各轮已知用量和调用次数，`tool_usage` 按机器人、群、用户、工具汇总使用情况；存在未知轮次时，用量汇总只包含已知部分。

统计库启用 WAL。回复发送成功后记录回复消息 ID，并在日志中输出本次请求的总输入、总输出 Token 和输入缓存命中率；输入缓存命中率使用命中输入 Token / 总输入 Token，缺少必要数据时显示未知。

群消息记录与业务处理独立，保留发送时间、发送者信息、子类型、完整原始正文 JSON 和引用关系，包括未触发请求、机器人自身消息、允许列表内的黑名单群消息和未支持消息段。正文保留数组或 CQ 字符串原值，重复上报逐次插入；记录失败写入日志，不阻塞业务。记录与缓存不自动作为聊天历史上下文，也不导入旧版数据库。

## 项目结构与插件扩展

解决方案位于 `QQAIAgent/QQAIAgent.sln`，各项目职责如下：

| 项目 / 目录 | 职责 |
| --- | --- |
| `QQAIAgent/Config` | 业务 JSON 配置模板与系统提示词 |
| `QQAIAgent.Host` | 入口、依赖注入、消息队列、日志和插件加载 |
| `QQAIAgent.Agent` | 模型接口、多轮请求处理、工具执行与统计 |
| `QQAIAgent.Abstractions` | 插件契约、思考强度和请求工具上下文 |
| `QQAIAgent.Infrastructure` | SQLite、群消息、转发及图片文件 ID 存储 |
| `QQAIAgent.Messaging` | 群消息与内容类型、读取及发送接口 |
| `QQAIAgent.NapCat` | NapCat WebSocket 与协议解析 |
| `DeepSeek` | DeepSeek HTTP 模型适配 |
| `QQAIAgent.Tools` | 五个内置工具的插件实现 |
| `Third` | 第三方程序的本地存放目录 |

Host 直接注册 `DeepSeekChatModel` 为 `IChatModel` 单例；工具通过插件目录加载。当前插件契约见 [ITool.cs](QQAIAgent/QQAIAgent.Abstractions/Tool/ITool.cs)：

```csharp
string Name { get; }
Task<string> ExecuteAsync(string argumentsJson, ToolContext context, CancellationToken cancellationToken);
```

工具接收模型返回的参数 JSON 和本条请求共享的上下文，返回可交给模型的文本或 JSON 字符串。基础设施功能通过普通 C# API 提供，工具负责适配模型参数与结果。

添加插件时：

1. 创建 `net10.0` 类库，实现公开、非抽象的 `ITool` 类型；工具名称保持唯一。
2. 设置 `EnableDynamicLoading=true`，引用 `QQAIAgent.Abstractions` 时使用 `Private=false`、`ExcludeAssets=runtime`。
3. 将完整插件输出放到宿主的 `Plugins/程序集名称/`，目录内保留同名入口 DLL、依赖和运行资源；在 `tools.local.json` 添加并启用对应定义，然后重启。
4. 若需随 Host 构建与发布，在 Host 的项目引用中设置 `ReferenceOutputAssembly=false`、`Private=false` 和 `OutputItemType=ToolPluginAssembly`，沿用内置插件的复制流程。

Host 只扫描 `Plugins/` 的直接子目录，没有同名入口 DLL 的目录会跳过。每个插件使用独立 `AssemblyLoadContext` 解析依赖，共享宿主的契约程序集；工具作为单例注册，同一实例可能被多个请求并发调用，临时状态应放在方法内部或请求上下文中。

## 使用协议

本项目采用自定义限制性许可，完整条款见 [LICENSE](LICENSE)：

- 仅供非商业学习、研究使用，禁止商业使用。
- 未经相应著作权人书面授权，不得二次开发。
- 在适用法律允许的范围内，未经授权使用造成的损失由使用者自行承担。
- 第三方组件遵守各自许可证，本项目协议不替代第三方许可证。
