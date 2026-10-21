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

正常输出写入标准输出，错误信息写入标准错误。退出码 `0` 表示成功，`1` 表示执行失败，`2` 表示命令或参数错误；PowerShell 可通过 `$LASTEXITCODE` 读取退出码。

## 分词测试

CLI 引用 `QQAIAgent.Infrastructure`，调用其中的 `JiebaTokenizer`。Infrastructure 负责词典初始化、多用户词典加载和分词；CLI 负责参数解析、配置读取、计时和输出，不访问数据库。

```powershell
# 查看参数
.\QQAIAgent.Cli.exe tokenize --help

# 按默认配置加载用户词典，使用精确模式
.\QQAIAgent.Cli.exe tokenize --text "小明毕业于中国科学院计算所"

# 搜索模式：输出较细的词元，便于后续全文检索
.\QQAIAgent.Cli.exe tokenize --text "原神角色怎么配队" --mode search

# 测试包含链接的文本
.\QQAIAgent.Cli.exe tokenize --text "原神攻略 https://example.com/guide?q=team 配队" --mode search

# 全模式
.\QQAIAgent.Cli.exe tokenize --text "我来到北京清华大学" --mode full

# 使用其他分词配置；--config 相对路径以当前工作目录为基准
.\QQAIAgent.Cli.exe tokenize --text "测试自定义游戏术语" --config "D:\Bot\tokenizer.local.json"
```

### 配置用户词典

默认读取程序目录下的 `Config/tokenizer.local.json`，模板为 `tokenizer.example.json`。用户词典通过 JSON 数组指定，替代原来的 `--userDict` 和 `--user-dict` 参数：

```json
{
  "UserDictionaryPaths": [
    "Dictionary/userDict.txt"
  ]
}
```

数组可以列出多个词典文件，按配置顺序加载。相对路径以配置文件所在目录为基准，也可以填写绝对路径。设为 `"UserDictionaryPaths": []` 时只使用 Jieba 内置词典；可准备两份配置，用 `--config` 对比不同词典的结果。程序只加载数组中明确指定的文件，不扫描目录。

构建和发布会复制两份分词配置，以及 `userDict.txt`、`LICENSE.userDict.txt` 和词库说明。添加其他用户词典时，应将对应文件部署到配置指定的位置。仅作对比的 `jieba.official.dict.txt` 和含空格短语存档不会复制或自动加载。

### 分词与计时

原始文本直接交给 Jieba 分词，包括其中的 URL。结果忽略空白词元，保留其他词元的顺序、重复项和标点；所有模式沿用库的默认 HMM 设置，输出数量为词元数量。

当前默认用户词典包含约 188 万条额外词条，每次启动 CLI 都会重新加载。初始化在 `JiebaTokenizer` 构造时完成，正式服务可复用同一个实例。CLI 在初始化后用相同文本和模式完整预热一次，再计时处理。显示的“分词处理耗时”包含分词及结果整理，不包含配置读取、词典加载、初始化、预热、结果格式化和控制台输出。

## 添加业务命令

1. 在 `Commands/` 下为具体命令创建独立文件，负责该命令的参数解析和执行。
2. 在 `Program.cs` 的分发分支中接入命令，并同步更新帮助文本。
3. 将可共享的业务逻辑放在已引用的 `QQAIAgent.Infrastructure` 中，CLI 只负责命令入口。
4. 命令需要异步执行时，将入口调整为异步等待命令结果；不要通过启动 Host 来执行独立命令。

目前使用直接分发，不引入命令插件、依赖注入或第三方命令行框架。
