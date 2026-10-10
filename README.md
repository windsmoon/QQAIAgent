# QQAIAgent

基于 NapCat 的 QQ AI Agent，计划使用 C# 和现代 .NET（.NET Core 的后续平台）开发，目标运行环境为 Windows Server。

## 项目目标

- **低内存占用**：限制消息队列、会话缓存和并发任务的规模；按需加载功能。后续测量内存时，分别记录 Agent 与 NapCat / QQ 进程的占用。
- **高度可配置**：通过配置管理连接参数、AI 提供商、模型、会话策略、功能开关及资源上限；本地密钥和运行数据不提交到 Git。
- **可扩展**：规划独立的协议适配、AI 提供商适配和功能扩展接口，支持经授权的扩展开发。
- **Windows Server 部署**：Agent 主程序采用 C# / .NET 后台应用，后续支持作为 Windows 服务运行。具体 .NET 版本及 Windows Server 版本将在创建解决方案时确定并验证。

## 当前目录

```text
QQAIAgent/
├── .gitignore          # .NET、IDE、第三方程序和本地运行数据忽略规则
├── LICENSE             # 学习用途许可及使用协议
├── README.md           # 项目说明
└── Third/
    ├── README.md       # 第三方依赖的目录约定、来源及许可说明
    └── NapCat/         # 预留的 NapCat 本地安装目录
```

当前仅完成仓库基础文件与第三方目录，尚未创建 C# 解决方案，也未安装 NapCat。后续 C# 工程与 `Third` 分开存放，由 Agent 的协议适配层对接 NapCat。

## 业务配置

业务 JSON 配置统一放在 `QQAIAgent/Config/`，与 `QQAIAgent.sln` 所在目录下的各项目文件夹同级。

- `napcat.example.json`：NapCat 连接与账号配置模板。
- `agent.example.json`：Agent 群白名单配置模板。
- `deepseek.example.json`：DeepSeek API Key、模型、完整请求地址及超时配置模板。

将模板复制为同名的 `*.local.json` 并填写实际配置，例如 `deepseek.local.json`。本地配置已由 Git 忽略。Host 启动时读取 NapCat 和 Agent 配置；DeepSeek 在自身模型实例创建时读取 `deepseek.local.json`。构建和发布时会把配置文件复制到程序目录的 `Config/`，运行时按程序目录定位配置，不依赖启动时的工作目录。

大模型通过 `IChatModel` 接口开放给外部扩展。具体提供商在自己的项目中加载和使用配置，Host 不引用或注册具体的大模型实现。

## 使用协议

本项目采用自定义限制性许可，完整条款见 [LICENSE](LICENSE)：

- 仅供非商业学习、研究使用，禁止商业使用。
- 未经相应著作权人书面授权，不得二次开发。
- 在适用法律允许的范围内，未经授权使用造成的损失由使用者自行承担。
- 第三方组件遵守各自许可证，本项目协议不替代第三方许可证。
