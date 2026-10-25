using System.Diagnostics;
using QQAIAgent.Infrastructure.Embedding;

namespace QQAIAgent.Cli.Commands;

internal static class EmbeddingCommand
{
    #region methods
    public static int Execute(string[] args)
    {
        if (args is [] or ["--help"] or ["-h"] or ["test", "--help"] or ["test", "-h"])
        {
            return ShowHelp();
        }
        if (args[0] != "test")
        {
            return ShowUsageError("embedding 子命令只能是 test。");
        }

        var options = new Dictionary<string, string>();
        for (var index = 1; index < args.Length; index++)
        {
            var option = args[index];
            if (option is not ("--text" or "--type" or "--directory"))
            {
                return ShowUsageError($"不支持的参数：{option}");
            }
            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return ShowUsageError($"参数 {option} 缺少值。");
            }
            options[option] = args[++index];
        }

        var text = options.GetValueOrDefault("--text", "");
        var type = options.GetValueOrDefault("--type", "passage");
        if (string.IsNullOrWhiteSpace(text))
        {
            return ShowUsageError("请通过 --text 指定非空文本。");
        }
        if (type is not ("passage" or "query"))
        {
            return ShowUsageError("--type 只能是 passage 或 query。");
        }

        try
        {
            var directory = Path.GetFullPath(options.GetValueOrDefault("--directory",
                Path.Combine(AppContext.BaseDirectory, "Third", "multilingual-e5-small")));
            Console.WriteLine($"模型目录：{directory}");
            Console.WriteLine($"类型：{type}");
            Console.WriteLine("正在加载模型和 Tokenizer……");
            var stopwatch = Stopwatch.StartNew();
            using var generator = new EmbeddingGenerator(directory);
            Console.WriteLine($"首次加载耗时：{stopwatch.Elapsed.TotalMilliseconds:F2} ms");

            // 先完整预热，再统计包含 Tokenizer、推理、池化和归一化的单句处理耗时。
            generator.Generate(text, type);
            stopwatch.Restart();
            var vector = generator.Generate(text, type);
            stopwatch.Stop();

            Console.WriteLine($"向量维度：{vector.Length}");
            Console.WriteLine($"向量模长：{Math.Sqrt(vector.Sum(value => (double)value * value)):F6}");
            Console.WriteLine($"预热后处理耗时：{stopwatch.Elapsed.TotalMilliseconds:F2} ms");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"向量生成失败：{exception.Message}");
            return 1;
        }
    }

    private static int ShowHelp()
    {
        Console.WriteLine("""
            用法：
              QQAIAgent.Cli.exe embedding test --text "测试文本" [--type passage|query]
                  [--directory "模型目录"]

            --type        passage 用于聊天（默认），query 用于查询；自动添加对应前缀
            --directory   默认程序目录下 Third/multilingual-e5-small；显式相对路径按当前工作目录解析

            单句最多 512 tokens（含前缀和特殊标记），超长截断；输出 384 维 L2 归一化向量。
            首次加载单独计时；预热后处理耗时包含 Tokenizer、推理、Mean Pooling 和 L2 归一化。
            """);
        return 0;
    }

    private static int ShowUsageError(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine("使用 QQAIAgent.Cli.exe embedding --help 查看帮助。");
        return 2;
    }
    #endregion
}
