# 群消息全文索引

`GroupMessageFtsStore` 操作指定的已有群数据库，使用普通 FTS5 表 `messages_fts(tokens)`。分词结果由 FTS 保存，`rowid` 对应 `messages.id`；不创建外部内容表、不增加原消息列、不创建新的数据库文件。

## 调用方式

调用方先读取分词配置并创建 `Tokenizer`，随后可以顺序处理多个数据库，复用词典实例：

```csharp
var tokenizer = new Tokenizer(config, configDirectory);
var store = new GroupMessageFtsStore(databasePath, tokenizer);

// 每次清空 FTS 后从头建立全部消息的索引，默认每批 500 条；内部自动初始化 FTS 表。
var (count, elapsed) = await store.BuildAsync(cancellationToken: cancellationToken);

var messages = await store.SearchAsync(["角色", "配队"], ftsOperator: "and", limit: 20, cancellationToken: cancellationToken);
```

`InitializeAsync` 可以单独调用，只建立 FTS 表。`BuildAsync` 返回本次处理的消息数和处理耗时，内部在建表及连接初始化完成后开始计时；调用方应先完成词典加载和预热。

## 索引规则

- 建索引和搜索均固定使用 Jieba `search` 模式，开启 HMM；CLI 原有分词测试仍可选择模式。
- 沿用现有数据库搜索的正文提取规则：JSON 字符串直接使用，消息段数组按顺序拼接 `text` 段，不展开图片、卡片和合并转发。
- 正文中的 URL 正常参与分词。无文本的消息保存空词元行，与原消息保持一一对应。
- 查询接收关键词列表和必填的 `ftsOperator`（`and` 或 `or`）。关键词按搜索模式分词、去重并忽略纯标点词元；`and` 要求所有词元命中，`or` 命中任一词元即可。关键词中的双引号按 FTS 字面文本转义，不能注入查询运算符。按相关性排序，分数相同时按消息 id 倒序。返回完整原文和发送者等信息，不提供原文短语匹配或高亮。

## 全量重建

`messages` 按只新增的方式使用，不设置删除或更新触发器。`BuildAsync` 每次先清空 FTS，再按消息 id 从头分批读取，直到没有下一批消息；不判断是否已有索引，也不支持增量补建。

每批在同一个写事务中读取、分词并写入，保证整批提交或回滚；事务期间会占用数据库写锁。若新消息已实时写入索引，重建时覆盖对应的 FTS 行，避免主键冲突。

取消或失败会回滚当前批次，已提交批次保留，但再次调用仍会先清空并从头重建，不续接上次进度。重建期间搜索结果不完整。修改词典后再次调用 `BuildAsync` 即可重新分词，不能只调用 FTS 自身的 rebuild 命令。

## 新消息实时索引

Host 启动时读取 `Config/tokenizer.local.json`，按 `UserDictionaryPaths` 加载用户词典，并在所有群之间复用同一个 `Tokenizer`。默认用户词典及其许可证随 Host 构建和发布复制到 `Config/Dictionary`；官方词典存档不加载。自定义词典路径所指向的文件也需部署到服务器。

`GroupMessageStore.InitializeAsync` 为每个允许记录的群创建 `GroupMessageFtsStore`，由它初始化 FTS 表，不清空已有索引，也不自动处理历史消息。`GroupMessageStore.RecordAsync` 从原始 `ContentJson` 提取文本，保存原消息后，将记录 ID 和文本传给 `GroupMessageFtsStore.RecordAsync`，由后者使用 `search` 模式分词并写入 FTS，不再查询原消息。两个步骤共用同一个连接和事务，由消息存储统一提交；任一步失败均不提交。无文本消息也保存空词元行。

历史消息通过 CLI `fts build --group <群号|all>` 全量重建，`fts search --group <群号> --text "查询文本"` 可直接测试全文搜索；两者均支持 `--directory` 指定群数据库目录。原有群消息搜索接口仍未切换到 FTS。
