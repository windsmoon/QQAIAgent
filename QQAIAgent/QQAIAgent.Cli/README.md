# QQAIAgent.Cli

独立的 .NET 10 控制台程序，用于执行一次性命令。每次调用执行完成后退出，不启动 QQAIAgent.Host 服务。

## 使用方式

编译后，在可执行文件所在目录运行：

```powershell
.\QQAIAgent.Cli.exe
.\QQAIAgent.Cli.exe help
.\QQAIAgent.Cli.exe --help
.\QQAIAgent.Cli.exe version
.\QQAIAgent.Cli.exe --version
```

`-h` 等同于 `--help`，`-v` 等同于 `--version`。命令区分大小写；帮助和版本命令不接受额外参数。

正常输出写入标准输出，命令或参数错误写入标准错误。退出码 `0` 表示成功，`2` 表示命令或参数错误；PowerShell 可通过 `$LASTEXITCODE` 读取退出码。

当前只提供帮助、版本和命令分发入口，尚未接入分词、数据库或其他业务操作，也不读取服务配置。

## 添加业务命令

1. 在 `Commands/` 下为具体命令创建独立文件，负责该命令的参数解析和执行。
2. 在 `Program.cs` 的分发分支中接入命令，并同步更新帮助文本。
3. 需要复用数据库等功能时，再添加对 `QQAIAgent.Infrastructure` 的项目引用，将可共享的业务逻辑放在 Infrastructure 中。
4. 命令需要异步执行时，将入口调整为异步等待命令结果；不要通过启动 Host 来执行独立命令。

目前使用直接分发，不引入命令插件、依赖注入或第三方命令行框架。
