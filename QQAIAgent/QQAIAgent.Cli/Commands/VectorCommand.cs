using System.Diagnostics;
using System.Globalization;
using QQAIAgent.Infrastructure.Embedding;
using QQAIAgent.Infrastructure.Messages;

namespace QQAIAgent.Cli.Commands;

internal static class VectorCommand
{
    #region methods
    public static async Task<int> ExecuteAsync(string[] args)
    {
        if (args is [] or ["--help"] or ["-h"] or ["build", "--help"] or ["build", "-h"]
            or ["search", "--help"] or ["search", "-h"])
        {
            return ShowHelp();
        }
        if (args[0] is not ("build" or "search"))
        {
            return ShowUsageError("vector 子命令只能是 build 或 search。");
        }

        var isBuild = args[0] == "build";
        var options = new Dictionary<string, string>();
        for (var index = 1; index < args.Length; index++)
        {
            var option = args[index];
            if (option is not ("--group" or "--directory" or "--model-directory")
                && (isBuild || option is not ("--text" or "--limit")))
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
        if (isBuild == false && string.IsNullOrWhiteSpace(text))
        {
            return ShowUsageError("search 必须通过 --text 指定非空查询文本。");
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

            var modelDirectory = Path.GetFullPath(options.GetValueOrDefault("--model-directory",
                Path.Combine(AppContext.BaseDirectory, "Third", "multilingual-e5-small")));
            Console.WriteLine($"正在加载模型并预热：{modelDirectory}");
            using var generator = new EmbeddingGenerator(modelDirectory);
            generator.Generate(isBuild ? "群聊历史消息" : text, isBuild ? "passage" : "query");

            long totalCount = 0;
            long totalSkipped = 0;
            var totalElapsed = TimeSpan.Zero;
            foreach (var path in paths)
            {
                Console.WriteLine($"群：{Path.GetFileNameWithoutExtension(path)}；数据库：{path}");
                var store = new GroupMessageVectorStore(path);
                if (isBuild)
                {
                    var result = await store.BuildAsync(generator);
                    totalCount += result.Count;
                    totalSkipped += result.Skipped;
                    totalElapsed += result.Elapsed;
                    Console.WriteLine($"重建完成：{result.Count} 条；跳过空文本：{result.Skipped} 条；处理耗时：{result.Elapsed.TotalMilliseconds:F2} ms");
                }
                else
                {
                    var stopwatch = Stopwatch.StartNew();
                    var results = await store.SearchAsync(text, generator, limit);
                    stopwatch.Stop();
                    Console.WriteLine($"返回：{results.Count} 条；查询耗时：{stopwatch.Elapsed.TotalMilliseconds:F2} ms");
                    for (var index = 0; index < results.Count; index++)
                    {
                        var (message, distance) = results[index];
                        Console.WriteLine($"#{index + 1} 距离={distance:F6} 记录ID={message.RecordId} 消息ID={message.MessageId} 发送者={message.SenderNickname}({message.SenderId}) 时间戳={message.MessageTime}");
                        Console.WriteLine(message.Text);
                    }
                }
            }
            if (isBuild)
            {
                Console.WriteLine($"合计：{paths.Count} 个群，{totalCount} 条；跳过空文本：{totalSkipped} 条；处理耗时：{totalElapsed.TotalMilliseconds:F2} ms");
            }
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"向量 {args[0]} 失败：{exception.Message}");
            return 1;
        }
    }

    private static int ShowHelp()
    {
        Console.WriteLine("""
            用法：
              QQAIAgent.Cli.exe vector build --group <群号|all>
                  [--directory "群数据库目录"] [--model-directory "模型目录"]
              QQAIAgent.Cli.exe vector search --group <群号> --text "查询文本" [--limit 20]
                  [--directory "群数据库目录"] [--model-directory "模型目录"]

            --group             指定群号；build 可用 all 顺序处理所有以正整数群号命名的 .sqlite3 文件
            --directory         默认程序目录下 Data/Messages
            --model-directory   默认程序目录下 Third/multilingual-e5-small
            --text              search 查询文本，自动添加 query 前缀
            --limit             search 最多返回条数，默认 20；余弦距离越小越相近
            显式传入的相对路径按当前工作目录解析。

            build 仅在停服维护期间执行。每次清空向量表后全量重建，中断后再次执行从头开始。
            每批读取 500 条，跳过空文本；所有群复用同一模型，使用 passage 前缀生成向量。
            处理耗时不包含模型加载、预热、建表及连接初始化；不创建新的数据库文件。
            search 不会自动建表或生成历史向量；首次检索前先执行 build。
            查询耗时包含查询向量生成及数据库检索，不包含模型加载和预热。
            """);
        return 0;
    }

    private static int ShowUsageError(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine("使用 QQAIAgent.Cli.exe vector --help 查看帮助。");
        return 2;
    }
    #endregion
}
