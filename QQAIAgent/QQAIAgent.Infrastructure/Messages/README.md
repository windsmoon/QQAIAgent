# 群消息全文索引

`GroupMessageFtsStore` 操作指定的已有群数据库，使用普通 FTS5 表 `messages_fts(tokens)`。分词结果由 FTS 保存，`rowid` 对应 `messages.id`；不创建外部内容表、不增加原消息列、不创建新的数据库文件。

## 调用方式

调用方先读取分词配置并创建 `Tokenizer`，随后可以顺序处理多个数据库，复用词典实例：

```csharp
var tokenizer = new Tokenizer(config, configDirectory);
var store = new GroupMessageFtsStore(databasePath, tokenizer);

// 默认每批 500 条，只处理尚无 FTS 记录的消息；内部自动初始化表和触发器。
var count = await store.BuildAsync(cancellationToken: cancellationToken);

// 词典变更后必须重新分词，不能只调用 FTS 自身的 rebuild 命令。
var rebuiltCount = await store.BuildAsync(rebuild: true, cancellationToken: cancellationToken);

var messages = await store.SearchAsync("角色 配队", limit: 20, cancellationToken: cancellationToken);
```

`InitializeAsync` 可以单独调用，只建立表和触发器。`BuildAsync` 返回本次处理的消息数；需要统计处理耗时时，由调用方在词典初始化、预热完成后开始计时。

## 索引规则

- 建索引和搜索均固定使用 Jieba `search` 模式，开启 HMM；CLI 原有分词测试仍可选择模式。
- 沿用现有数据库搜索的正文提取规则：JSON 字符串直接使用，消息段数组按顺序拼接 `text` 段，不展开图片、卡片和合并转发。
- 正文中的 URL 正常参与分词。无文本的消息保存空词元行，作为已处理标记。
- 查询词以 AND 组合，忽略纯标点词元，转义 FTS 双引号；按相关性排序，分数相同时按消息 id 倒序。返回完整原文和发送者等信息，不提供原文短语匹配或高亮。

## 增量与重建

原消息删除时，触发器删除对应 FTS 记录；原消息 id 或正文改变时，触发器使旧索引失效。触发器不调用 Jieba，新增及修改后的消息由下一次 `BuildAsync` 补建。

每批在同一个写事务中读取、分词并写入，避免并发正文修改产生旧索引；事务期间会占用数据库写锁。处理上限为开始时的最大消息 id，执行期间新增的更大 id 和已处理批次之后的正文修改留待下一次补建。

取消或失败会回滚当前批次，已提交批次保留。全量重建先清空 FTS，随后逐批填充，期间搜索结果不完整；中断后使用相同词典普通补建即可继续。修改词典后应重新发起全量重建。

当前只提供 Infrastructure 数据库接口，尚未接入 CLI 的索引命令或 Host 的实时分词写入；原有群消息搜索接口也未切换到 FTS。
