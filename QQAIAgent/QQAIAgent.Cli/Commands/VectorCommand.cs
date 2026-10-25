using System.Globalization;
using QQAIAgent.Infrastructure.Embedding;
using QQAIAgent.Infrastructure.Messages;

namespace QQAIAgent.Cli.Commands;

internal static class VectorCommand
{
    #region methods
    public static async Task<int> ExecuteAsync(string[] args)
    {
        if (args is [] or ["--help"] or ["-h"] or ["build", "--help"] or ["build", "-h"])
        {
            return ShowHelp();
        }
        if (args[0] != "build")
        {
            return ShowUsageError("vector 子命令只能是 build。");
        }

        var options = new Dictionary<string, string>();
        for (var index = 1; index < args.Length; index++)
        {
            var option = args[index];
            if (option is not ("--group" or "--directory" or "--model-directory"))
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
        if (group != "all" && (long.TryParse(group, NumberStyles.None, CultureInfo.InvariantCulture, out var groupId) == false || groupId <= 0))
        {
            return ShowUsageError("--group 必须指定正整数群号或 all。");
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
            generator.Generate("群聊历史消息", "passage");

            long totalCount = 0;
            long totalSkipped = 0;
            var totalElapsed = TimeSpan.Zero;
            foreach (var path in paths)
            {
                Console.WriteLine($"群：{Path.GetFileNameWithoutExtension(path)}；数据库：{path}");
                var store = new GroupMessageVectorStore(path);
                var result = await store.BuildAsync(generator);
                totalCount += result.Count;
                totalSkipped += result.Skipped;
                totalElapsed += result.Elapsed;
                Console.WriteLine($"重建完成：{result.Count} 条；跳过空文本：{result.Skipped} 条；处理耗时：{result.Elapsed.TotalMilliseconds:F2} ms");
            }
            Console.WriteLine($"合计：{paths.Count} 个群，{totalCount} 条；跳过空文本：{totalSkipped} 条；处理耗时：{totalElapsed.TotalMilliseconds:F2} ms");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"向量重建失败：{exception.Message}");
            return 1;
        }
    }

    private static int ShowHelp()
    {
        Console.WriteLine("""
            用法：
              QQAIAgent.Cli.exe vector build --group <群号|all>
                  [--directory "群数据库目录"] [--model-directory "模型目录"]

            --group             指定群号，或用 all 顺序处理所有以正整数群号命名的 .sqlite3 文件
            --directory         默认程序目录下 Data/Messages
            --model-directory   默认程序目录下 Third/multilingual-e5-small
            显式传入的相对路径按当前工作目录解析。

            仅在停服维护期间执行。每次清空向量表后全量重建，中断后再次执行从头开始。
            每批读取 500 条，跳过空文本；所有群复用同一模型，使用 passage 前缀生成向量。
            处理耗时不包含模型加载、预热、建表及连接初始化；不创建新的数据库文件。
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
