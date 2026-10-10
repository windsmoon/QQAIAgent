# QQAIAgent

基于 NapCat 的 QQ AI Agent，使用 C# 和 .NET 10 开发，目标运行环境为 Windows / Windows Server。设计目标是低内存占用、高度可配置和可扩展。

## 当前功能

通过 NapCat 的正向 WebSocket 接收群消息，并核验登录的机器人账号。白名单群中以 `123` 开头的消息会进入处理队列，例如 `123 你好`。

`GroupMessageAgent` 去掉触发前缀，将当前问题作为一条用户消息传给 `IChatModel`，调用一次模型后把返回文本发送到原群。只有前缀而没有问题时，直接回复“输入内容为空”；模型没有返回可用文本时，回复“回复内容为空”。

- 所有群共用一个容量为 100 的消息队列，按顺序完成模型请求和 QQ 回复。
- 模型实例作为单例复用，每条消息单独创建 `ChatRequest`，当前不保存历史上下文。
- DeepSeek 在自己的项目中读取配置，并维护一个复用的 `HttpClient`。
- 当前仅实现单次文本问答，不使用工具调用或 Agent 循环（agent loop）。

## 目录结构

```text
QQAIAgent/
├── QQAIAgent/
│   ├── QQAIAgent.sln
│   ├── Config/                # 各模块的 JSON 配置与模板
│   ├── QQAIAgent.Host/        # 程序入口、依赖注入与消息队列
│   ├── QQAIAgent.Agent/       # 群消息处理、模型接口与通用加载器
│   ├── QQAIAgent.Messaging/   # 群消息数据与发送接口
│   ├── QQAIAgent.NapCat/      # NapCat 协议适配
│   └── DeepSeek/             # 独立的大模型实现
├── Third/                    # 第三方程序的本地存放目录
├── LICENSE
└── README.md
```

模型通过 `IChatModel` 接口开放给外部扩展。Host 只使用通用接口和加载器，不引用具体的大模型项目；提供商在自己的项目中处理配置和通信。

## 业务配置

业务 JSON 统一放在 `QQAIAgent/Config/`，与解决方案中的各项目文件夹同级。将模板复制为对应的 `*.local.json`，再填写实际配置；本地配置由 Git 忽略。

| 模板 | 本地配置 | 内容 | 读取方 |
| --- | --- | --- | --- |
| `napcat.example.json` | `napcat.local.json` | WebSocket 地址、访问令牌、机器人 QQ 账号 | Host |
| `agent.example.json` | `agent.local.json` | 群白名单、模型 DLL 路径、实现类型 | Host |
| `deepseek.example.json` | `deepseek.local.json` | API Key、模型名称、完整请求地址、超时秒数 | DeepSeek 模型 |

构建和发布 Host 时，配置文件会复制到宿主程序目录的 `Config/`。运行时以 `AppContext.BaseDirectory` 定位文件，不依赖启动时的工作目录。DeepSeek 在模型实例创建时读取自己的配置，配置对象不注册到 Host 的依赖注入容器中。

`agent.local.json` 示例：

```json
{
  "AllowedGroups": [123456789],
  "ChatModelAssembly": "Extensions/DeepSeek/DeepSeek.dll",
  "ChatModelType": "DeepSeek.DeepSeekChatModel"
}
```

`AllowedGroups` 填写允许处理的真实群号，空列表表示不处理任何群。`ChatModelAssembly` 支持绝对路径，也支持相对于宿主程序目录的路径。`ChatModelType` 填写包含命名空间的类型名称。

## 本地运行

1. 准备 .NET 10 SDK，并在 NapCat 中配置正向 WebSocket 服务。
2. 在 Rider 中打开 `QQAIAgent/QQAIAgent.sln`，复制并填写三份本地配置。`napcat.local.json` 的地址、令牌和机器人账号应与 NapCat 配置对应。
3. 本地开发时，将 `ChatModelAssembly` 填为 DeepSeek 项目输出 DLL 的绝对路径，例如 `<仓库目录>/QQAIAgent/DeepSeek/bin/Debug/net10.0/DeepSeek.dll`。
4. 构建整个解决方案，确保生成 DeepSeek 模型 DLL，然后启动 `QQAIAgent.Host`。只构建 Host 不会构建 DeepSeek 项目。
5. 在白名单群中发送 `123 你好`，由模型生成文本后回复到该群。

修改源目录中的 JSON 后，需要将更新后的配置复制到宿主程序目录的 `Config/`；重新构建 Host 会按项目配置执行复制。配置在启动或模型实例创建时读取，更新后需要重新启动宿主才能生效。

## 接入模型扩展

扩展项目引用 `QQAIAgent.Agent`，实现 `IChatModel`，并提供公开的无参构造函数。模型的专用配置、HTTP 客户端和其他通信细节由扩展自己维护；请求上下文通过 `ChatRequest.Messages` 传入。

Host 根据 `ChatModelAssembly` 和 `ChatModelType` 调用 `ChatModelLoader` 创建实例，注册为 `IChatModel` 单例，再通过构造函数传给 `GroupMessageAgent`。若模型实现了 `IDisposable`，宿主退出时由依赖注入容器释放。

发布时，按配置路径部署模型 DLL，将模型额外的托管依赖放在同一目录。沿用宿主中的 `QQAIAgent.Agent.dll` 和 `QQAIAgent.Messaging.dll` 作为共享接口程序集。Host 项目不自动构建或复制模型 DLL，需要单独完成模型的构建和部署。

使用模板中的相对路径时，宿主程序目录应包含：

```text
程序目录/
├── QQAIAgent.Host.exe
├── QQAIAgent.Agent.dll
├── QQAIAgent.Messaging.dll
├── Config/
│   ├── napcat.local.json
│   ├── agent.local.json
│   └── deepseek.local.json
└── Extensions/
    └── DeepSeek/
        └── DeepSeek.dll
```

以上仅列出主要文件，宿主的其他运行依赖仍需随发布输出一起部署。

## 使用协议

本项目采用自定义限制性许可，完整条款见 [LICENSE](LICENSE)：

- 仅供非商业学习、研究使用，禁止商业使用。
- 未经相应著作权人书面授权，不得二次开发。
- 在适用法律允许的范围内，未经授权使用造成的损失由使用者自行承担。
- 第三方组件遵守各自许可证，本项目协议不替代第三方许可证。
