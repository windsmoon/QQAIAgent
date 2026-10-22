# 群消息全文索引

`GroupMessageFtsStore` 操作指定的已有群数据库，使用普通 FTS5 表 `messages_fts(tokens)`。分词结果由 FTS 保存，`rowid` 对应 `messages.id`；不创建外部内容表、不增加原消息列、不创建新的数据库文件。

## 调用方式

调用方先读取分词配置并创建 `Tokenizer`，随后可以顺序处理多个数据库，复用词典实例：

```csharp
var tokenizer = new Tokenizer(config, configDirectory);
var store = new GroupMessageFtsStore(databasePath, tokenizer);

// 每次清空 FTS 后从头建立全部消息的索引，默认每批 500 条；内部自动初始化 FTS 表。
var count = await store.BuildAsync(cancellationToken: cancellationToken);

var messages = await store.SearchAsync("角色 配队", limit: 20, cancellationToken: cancellationToken);
```

`InitializeAsync` 可以单独调用，只建立 FTS 表。`BuildAsync` 返回本次处理的消息数；需要统计处理耗时时，由调用方在词典初始化、预热完成后开始计时。

## 索引规则

- 建索引和搜索均固定使用 Jieba `search` 模式，开启 HMM；CLI 原有分词测试仍可选择模式。
- 沿用现有数据库搜索的正文提取规则：JSON 字符串直接使用，消息段数组按顺序拼接 `text` 段，不展开图片、卡片和合并转发。
- 正文中的 URL 正常参与分词。无文本的消息保存空词元行，与原消息保持一一对应。
- 查询词以 AND 组合，忽略纯标点词元，转义 FTS 双引号；按相关性排序，分数相同时按消息 id 倒序。返回完整原文和发送者等信息，不提供原文短语匹配或高亮。

## 全量重建

`messages` 按只新增的方式使用，不设置删除或更新触发器。`BuildAsync` 每次先清空 FTS，再按消息 id 从头分批读取，直到没有下一批消息；不判断是否已有索引，也不支持增量补建。

每批在同一个写事务中读取、分词并写入，保证整批提交或回滚；事务期间会占用数据库写锁。

取消或失败会回滚当前批次，已提交批次保留，但再次调用仍会先清空并从头重建，不续接上次进度。重建期间搜索结果不完整。修改词典后再次调用 `BuildAsync` 即可重新分词，不能只调用 FTS 自身的 rebuild 命令。

当前只提供 Infrastructure 数据库接口，尚未接入 CLI 的索引命令或 Host 的实时分词写入；原有群消息搜索接口也未切换到 FTS。
