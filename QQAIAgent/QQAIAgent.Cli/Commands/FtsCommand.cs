using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using QQAIAgent.Infrastructure.Messages;
using QQAIAgent.Infrastructure.Tokenization;

namespace QQAIAgent.Cli.Commands;

internal static class FtsCommand
{
    #region methods
    public static async Task<int> ExecuteAsync(string[] args)
    {
        if (args is [] or ["--help"] or ["-h"] or ["build", "--help"] or ["search", "--help"]
            or ["build", "-h"] or ["search", "-h"])
        {
            return ShowHelp();
        }
        if (args[0] is not ("build" or "search"))
        {
            return ShowUsageError("fts 子命令只能是 build 或 search。");
        }

        var isBuild = args[0] == "build";
        var options = new Dictionary<string, string>();
        for (var index = 1; index < args.Length; index++)
        {
            var option = args[index];
            if (option is not ("--group" or "--directory" or "--config")
                && (isBuild || option is not ("--text" or "--operator" or "--limit")))
            {
                return ShowUsageError($"不支持的参数：{option}");
            }
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return ShowUsageError($"参数 {option} 缺少值。");
            }
            options[option] = args[++index];
        }

        var group = options.GetValueOrDefault("--group", "");
        if ((isBuild && group == "all") == false
            && (long.TryParse(group, NumberStyles.None, CultureInfo.InvariantCulture, out var groupId) == false || groupId <= 0))
        {
            return ShowUsageError(isBuild ? "--group 必须指定正整数群号或 all。" : "search 必须通过 --group 指定一个正整数群号。");
        }
        var text = options.GetValueOrDefault("--text", "");
        var ftsOperator = options.GetValueOrDefault("--operator", "or");
        if (isBuild == false && string.IsNullOrWhiteSpace(text))
        {
            return ShowUsageError("search 必须通过 --text 指定非空查询文本。");
        }
        if (ftsOperator is not ("and" or "or"))
        {
            return ShowUsageError("--operator 只能是 and 或 or。");
        }
        if (int.TryParse(options.GetValueOrDefault("--limit", "20"), out var limit) == false || limit < 1)
        {
            return ShowUsageError("--limit 必须是正整数。");
        }

        try
        {
            var directory = Path.GetFullPath(options.GetValueOrDefault("--directory",
                Path.Combine(AppContext.BaseDirectory, "Data", "Messages")));
            List<string> paths = group == "all"
                ? Directory.EnumerateFiles(directory, "*.sqlite3")
                    .Where(path => long.TryParse(Path.GetFileNameWithoutExtension(path), NumberStyles.None,
                        CultureInfo.InvariantCulture, out var id) && id > 0)
                    .Order(StringComparer.Ordinal).ToList()
                : [Path.Combine(directory, $"{long.Parse(group, CultureInfo.InvariantCulture)}.sqlite3")];
            if (paths.Count == 0)
            {
                Console.WriteLine($"目录中没有群消息数据库：{directory}");
                return 0;
            }

            var configPath = Path.GetFullPath(options.GetValueOrDefault("--config",
                Path.Combine(AppContext.BaseDirectory, "Config", "tokenizer.local.json")));
            Console.WriteLine("正在加载词典并预热……");
            var config = JsonSerializer.Deserialize<TokenizerConfig>(File.ReadAllText(configPath));
            var tokenizer = new Tokenizer(config, Path.GetDirectoryName(configPath));
            // 预热
            tokenizer.Tokenize("衬衫的价格是九磅十五便士", "search");
            
            long totalCount = 0;
            var totalElapsed = TimeSpan.Zero;
            foreach (var path in paths)
            {
                var store = new GroupMessageFtsStore(path, tokenizer);
                Console.WriteLine($"群：{Path.GetFileNameWithoutExtension(path)}；数据库：{path}");
                if (isBuild)
                {
                    var result = await store.BuildAsync();
                    totalCount += result.Count;
                    totalElapsed += result.Elapsed;
                    Console.WriteLine($"重建完成：{result.Count} 条；处理耗时：{result.Elapsed.TotalMilliseconds:F2} ms");
                }
                else
                {
                    var stopwatch = Stopwatch.StartNew();
                    var messages = await store.SearchAsync([text], ftsOperator, limit) ?? [];
                    stopwatch.Stop();
                    Console.WriteLine($"匹配方式：{ftsOperator}；返回：{messages.Count} 条；查询耗时：{stopwatch.Elapsed.TotalMilliseconds:F2} ms");
                    for (var index = 0; index < messages.Count; index++)
                    {
                        var message = messages[index];
                        Console.WriteLine($"#{index + 1} 记录ID={message.RecordId} 消息ID={message.MessageId} 发送者={message.SenderNickname}({message.SenderId}) 时间戳={message.MessageTime}");
                        Console.WriteLine(message.Text);
                    }
                }
            }
            if (isBuild && paths.Count > 1)
            {
                Console.WriteLine($"合计：{paths.Count} 个群，{totalCount} 条；处理耗时：{totalElapsed.TotalMilliseconds:F2} ms");
            }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FTS {args[0]} 失败：{exception.Message}");
            return 1;
        }
    }

    private static int ShowHelp()
    {
        Console.WriteLine("""
            用法：
              QQAIAgent.Cli.exe fts build --group <群号|all> [--directory "群数据库目录"] [--config "分词配置路径"]
              QQAIAgent.Cli.exe fts search --group <群号> --text "查询文本" [--operator and|or] [--limit 20]
                  [--directory "群数据库目录"] [--config "分词配置路径"]

            --group       build 可填 all，处理目录中所有以正整数群号命名的 .sqlite3 文件；search 只允许单个群号
            --directory   默认程序目录下 Data/Messages；显式传入的相对路径按当前工作目录解析
            --config      默认程序目录下 Config/tokenizer.local.json；词典路径相对于配置文件
            --text        search 查询文本，固定使用 search 模式分词
            --operator    and 全部词元命中，or 任一词元命中（默认 or）
            --limit       search 最多返回条数，默认 20；按相关性排序，分数相同时按记录 ID 倒序

            build 每次清空 FTS 后全量重建，处理耗时不包含词典加载、预热、建表及连接初始化。
            search 不会自动建表或重建索引；首次使用前先执行 build。
            """);
        return 0;
    }

    private static int ShowUsageError(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine("使用 QQAIAgent.Cli.exe fts --help 查看帮助。");
        return 2;
    }
    #endregion
}
