# 万象 Jieba 额外词典

## 文件

- `userDict.txt`：可供 Acme.Jieba.NET 的 `LoadUserDict` 加载，合并去重、剔除 Jieba 官方默认词典已有词条并移除纯英文字母词条后共 **1,884,355** 条，24,620,347 字节。
- `wanxiang.phrases.txt`：另存的 **1,862** 条含空白字符的短语。用于保留来源内容，不应直接作为 Jieba 用户词典加载。
- `jieba.official.dict.txt`：Jieba 官方默认词典原文件，5,071,852 字节，保留词语、词频和词性，**仅用于后续比对，不加载使用**。
- `LICENSE.userDict.txt`：上游 CC BY 4.0 许可证原文。
- `LICENSE.jieba.txt`：Jieba 官方 MIT 许可证原文，适用于 `jieba.official.dict.txt`。

两个万象词汇文件均使用 UTF-8 无 BOM、LF 换行，每行一个词条，按 Unicode 码点排序。Jieba 官方词典按下载内容原样保存。

## 来源和许可

- 项目：[amzxyz/rime-wanxiang-data](https://github.com/amzxyz/rime-wanxiang-data)。
- 作者与维护者：amzxyz 及万象词库贡献者。
- 来源目录：`dicts/`，包含简体词库、单字、英文、混合词、诗词及分类词库；本次提取该目录全部 18 个 `.dict.yaml` 文件，不包含 `dicts_hant/` 繁体词库、拼音数据库、模型或程序。
- 获取日期：2026-10-21（Asia/Shanghai）。
- 固定提交：[`25d2d5b30b3f3072ee8f823509e0690b84c7b81f`](https://github.com/amzxyz/rime-wanxiang-data/commit/25d2d5b30b3f3072ee8f823509e0690b84c7b81f)。
- 许可：[Creative Commons Attribution 4.0 International](https://creativecommons.org/licenses/by/4.0/)，适用范围见上游 [LICENSE-SCOPE.md](https://github.com/amzxyz/rime-wanxiang-data/blob/25d2d5b30b3f3072ee8f823509e0690b84c7b81f/LICENSE-SCOPE.md)。此处转换后的词库沿用 CC BY 4.0，分发时应保留来源、署名、许可和修改说明。

## 转换说明

1. 跳过 Rime YAML 表头、空行和注释，只提取数据行以制表符分隔的第一列词条。
2. 去除拼音、输入法权重及其他附加列；不把上游输入法权重直接当作 Jieba 词频。
3. 保留原始词形、大小写和标点，不做简繁转换，也不按词长或业务类别过滤。不同读音及不同文件中同形词条合并为一条。
4. 对不含空白字符的词条，按完整词形与 Jieba 官方默认词典做区分大小写的集合差集，剔除已有词，不按子串、前缀或词频判断重复。
5. 移除整个词条只由英文字母 A–Z / a–z 组成的条目（完整匹配 `[A-Za-z]+`，包含纯字母缩写）。中英混合、含数字或符号的词条原样保留。
6. 剩余词条写入 `userDict.txt`。词频和词性均省略，采用 Jieba 用户词典支持的可选字段格式，由分词器推算词频。
7. 含空白字符的条目原样另存到 `wanxiang.phrases.txt`，避免例如 `Veo 3` 的尾部被当作词频；不删除空格拼成新词，也不擅自拆词。此文件仅存档，不参与额外词典加载，也不应用用户词典的纯英文过滤规则。

这是上游完整 `dicts/` 提取词汇后相对于 Jieba 默认词典的增量，并已移除纯英文字母词条；尚未进行其他群聊场景筛选。实际加载的时间、内存及分词效果需在接入时测量。

## Jieba 去重基准

- 官方仓库：[fxsjy/jieba](https://github.com/fxsjy/jieba)。
- 基准文件：[`jieba/dict.txt`](https://github.com/fxsjy/jieba/blob/67fa2e36e72f69d9134b8a1037b83fbb070b9775/jieba/dict.txt)，使用默认词典，不使用可选的大词典或小词典。
- 固定提交：`67fa2e36e72f69d9134b8a1037b83fbb070b9775`。
- 获取日期：2026-10-21（Asia/Shanghai）。
- 基准文件 SHA-256：`7197c3211ddd98962b036cdf40324d1ea2bfaa12bd028e68faa70111a88e12a8`。
- 官方默认词典去重后：**349,045** 个词条。
- 已从本机安装的 `Acme.Jieba.NET 1.0.0` 只读提取 `Acme.Resources.dict.txt.gz`；解压后与下载的官方默认词典逐字节一致。
- 万象内部去重后：**2,186,591** 条；剔除默认词典已有词：**231,238** 条；得到 **1,955,353** 条；再移除纯英文字母词条 **70,998** 条，最终额外词典：**1,884,355** 条。

官方词典在本目录以 `jieba.official.dict.txt` 原样存档，仅作为比对基准，不加载、不替换分词器内嵌词典，也不并入额外词典。已核对最终额外词典与基准词表的交集为空。

## 加载方式

Acme.Jieba.NET 支持 `词语 [词频] [词性]` 用户词典格式，参见 [1.0.0 文档](https://www.nuget.org/packages/Acme.Jieba.NET/1.0.0)。项目通过 Infrastructure 中的 `JiebaTokenizer` 加载，CLI 默认读取 `Config/tokenizer.local.json`：

```json
{
  "UserDictionaryPaths": [
    "Dictionary/userDict.txt"
  ]
}
```

`UserDictionaryPaths` 数组支持多个用户词典，路径相对于配置文件所在目录；空数组仅使用内置词典。只加载明确列出的文件，不遍历本目录全部 TXT 文件。官方词典存档不应加入数组。CLI 测试命令为 `tokenize --text "待分词文本" --mode search`，可通过 `--config` 选择其他配置。构建和发布时复制分词配置、用户词典、许可证及本说明，官方词典和短语存档不随 CLI 分发或自动加载。尚未接入 Host 或进行数据库分词、索引。词库是共享资源；分词配置有对应的 `tokenizer.example.json` 和 `tokenizer.local.json`。

## 来源文件统计

“数据行数”包含同词多读音及跨文件重复；“含空白行数”是转入短语文件前的计数。

| 来源文件 | 数据行数 | 含空白行数 |
| --- | ---: | ---: |
| [abbrev.dict.yaml](https://github.com/amzxyz/rime-wanxiang-data/blob/25d2d5b30b3f3072ee8f823509e0690b84c7b81f/dicts/abbrev.dict.yaml) | 34,124 | 0 |
| [cuoyin.dict.yaml](https://github.com/amzxyz/rime-wanxiang-data/blob/25d2d5b30b3f3072ee8f823509e0690b84c7b81f/dicts/cuoyin.dict.yaml) | 196 | 0 |
| [diming.dict.yaml](https://github.com/amzxyz/rime-wanxiang-data/blob/25d2d5b30b3f3072ee8f823509e0690b84c7b81f/dicts/diming.dict.yaml) | 89,418 | 0 |
| [duoyin.dict.yaml](https://github.com/amzxyz/rime-wanxiang-data/blob/25d2d5b30b3f3072ee8f823509e0690b84c7b81f/dicts/duoyin.dict.yaml) | 4,733 | 0 |
| [en.dict.yaml](https://github.com/amzxyz/rime-wanxiang-data/blob/25d2d5b30b3f3072ee8f823509e0690b84c7b81f/dicts/en.dict.yaml) | 73,159 | 1,728 |
| [fangyan.dict.yaml](https://github.com/amzxyz/rime-wanxiang-data/blob/25d2d5b30b3f3072ee8f823509e0690b84c7b81f/dicts/fangyan.dict.yaml) | 336 | 0 |
| [huaxue.dict.yaml](https://github.com/amzxyz/rime-wanxiang-data/blob/25d2d5b30b3f3072ee8f823509e0690b84c7b81f/dicts/huaxue.dict.yaml) | 33,105 | 0 |
| [jichu.dict.yaml](https://github.com/amzxyz/rime-wanxiang-data/blob/25d2d5b30b3f3072ee8f823509e0690b84c7b81f/dicts/jichu.dict.yaml) | 1,493,800 | 0 |
| [mingren.dict.yaml](https://github.com/amzxyz/rime-wanxiang-data/blob/25d2d5b30b3f3072ee8f823509e0690b84c7b81f/dicts/mingren.dict.yaml) | 22,117 | 0 |
| [mixed.dict.yaml](https://github.com/amzxyz/rime-wanxiang-data/blob/25d2d5b30b3f3072ee8f823509e0690b84c7b81f/dicts/mixed.dict.yaml) | 1,808 | 136 |
| [shici.dict.yaml](https://github.com/amzxyz/rime-wanxiang-data/blob/25d2d5b30b3f3072ee8f823509e0690b84c7b81f/dicts/shici.dict.yaml) | 335,208 | 0 |
| [t9_abbrev.dict.yaml](https://github.com/amzxyz/rime-wanxiang-data/blob/25d2d5b30b3f3072ee8f823509e0690b84c7b81f/dicts/t9_abbrev.dict.yaml) | 31,532 | 0 |
| [taifeng.dict.yaml](https://github.com/amzxyz/rime-wanxiang-data/blob/25d2d5b30b3f3072ee8f823509e0690b84c7b81f/dicts/taifeng.dict.yaml) | 217 | 0 |
| [wuzhong.dict.yaml](https://github.com/amzxyz/rime-wanxiang-data/blob/25d2d5b30b3f3072ee8f823509e0690b84c7b81f/dicts/wuzhong.dict.yaml) | 66,332 | 0 |
| [yaopin.dict.yaml](https://github.com/amzxyz/rime-wanxiang-data/blob/25d2d5b30b3f3072ee8f823509e0690b84c7b81f/dicts/yaopin.dict.yaml) | 19,924 | 0 |
| [yiren.dict.yaml](https://github.com/amzxyz/rime-wanxiang-data/blob/25d2d5b30b3f3072ee8f823509e0690b84c7b81f/dicts/yiren.dict.yaml) | 14,444 | 0 |
| [yixue.dict.yaml](https://github.com/amzxyz/rime-wanxiang-data/blob/25d2d5b30b3f3072ee8f823509e0690b84c7b81f/dicts/yixue.dict.yaml) | 21,408 | 0 |
| [zi.dict.yaml](https://github.com/amzxyz/rime-wanxiang-data/blob/25d2d5b30b3f3072ee8f823509e0690b84c7b81f/dicts/zi.dict.yaml) | 51,050 | 0 |

## 文件摘要

- `jieba.official.dict.txt` SHA-256：`7197c3211ddd98962b036cdf40324d1ea2bfaa12bd028e68faa70111a88e12a8`
- `userDict.txt` SHA-256：`2fb5ca17bd28dab19fc255b423af3214bd2359004262fc3c1368a18fe2168549`
- `wanxiang.phrases.txt` SHA-256：`ce9be077cf426f3a1cc1a5e88a3da4535afc0b256da392ec87bb794984188a10`

下载时已将每份源词库的内容与该提交对应的 Git blob SHA-1 核对。
