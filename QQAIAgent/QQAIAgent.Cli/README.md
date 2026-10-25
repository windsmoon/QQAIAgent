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

CLI 引用 `QQAIAgent.Infrastructure`，调用其中的 `Tokenizer`。Infrastructure 负责词典初始化、多用户词典加载和分词；CLI 负责参数解析、配置读取、计时和输出，`tokenize` 命令不访问数据库。

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

当前默认用户词典包含约 188 万条额外词条，每次启动 CLI 都会重新加载。初始化在 `Tokenizer` 构造时完成，正式服务可复用同一个实例。CLI 在初始化后用相同文本和模式完整预热一次，再计时处理。显示的“分词处理耗时”包含分词及结果整理，不包含配置读取、词典加载、初始化、预热、结果格式化和控制台输出。

## FTS 重建与搜索

```powershell
# 重建单个群的全部历史消息索引
.\QQAIAgent.Cli.exe fts build --group 1129405252 --directory "D:\DevFile\QQAIAgent\Data\Messages"

# 重建该目录中所有群的索引，一次加载词典，逐群处理
.\QQAIAgent.Cli.exe fts build --group all --directory "D:\DevFile\QQAIAgent\Data\Messages"

# 必须指定一个群；按相关性输出排名和原消息
.\QQAIAgent.Cli.exe fts search --group 1129405252 --text "角色 配队" --operator and --limit 20 --directory "D:\DevFile\QQAIAgent\Data\Messages"
```

`--directory` 默认程序目录下的 `Data/Messages`，显式传入的相对路径以当前工作目录为基准。群数据库文件名为 `<群号>.sqlite3`；`all` 只扫描指定目录中以正整数群号命名的文件，不递归、不创建新数据库。这里的“所有群”指该目录已有的全部群数据库，不读取 Host 的允许群列表。两种命令均支持 `--config`，规则与 `tokenize` 相同。

`build` 每次清空 FTS 并全量重建，输出各群处理条数和毫秒耗时，多群时额外输出总数及耗时之和。处理耗时包含清空索引、读取历史消息、分词和事务写入，不包含词典加载、预热、建表、连接初始化及控制台输出。重建失败或中断后可能只有部分索引，重新运行会从头开始。

`search` 固定使用搜索模式分词，`--operator` 默认为 `or`，也可指定 `and`；`--limit` 默认为 20。输出的 `#1`、`#2` 等表示返回顺序，按相关性排序，分数相同时按记录 ID 倒序；同时显示原消息、发送者和 Unix 秒时间戳。查询耗时包含查询分词、数据库查询和结果读取，不含词典加载、预热和控制台输出。搜索不会自动建立或重建索引，首次使用前先运行 `fts build`。

## 添加业务命令

1. 在 `Commands/` 下为具体命令创建独立文件，负责该命令的参数解析和执行。
2. 在 `Program.cs` 的分发分支中接入命令，并同步更新帮助文本。
3. 将可共享的业务逻辑放在已引用的 `QQAIAgent.Infrastructure` 中，CLI 只负责命令入口。
4. 命令需要异步执行时，将入口调整为异步等待命令结果；不要通过启动 Host 来执行独立命令。

目前使用直接分发，不引入命令插件、依赖注入或第三方命令行框架。
