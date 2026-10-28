using QQAIAgent.Infrastructure.Runtime;
using System.Diagnostics;
using System.Text.Json;
using QQAIAgent.Infrastructure.Tokenization;

namespace QQAIAgent.Cli.Commands;

internal static class TokenizeCommand
{
    #region methods
    public static int Execute(string[] args)
    {
        if (args is [] or ["--help"] or ["-h"])
        {
            return ShowHelp();
        }

        string text = null;
        var configPath = Path.Combine(ApplicationPaths.RootDirectory, "Config", "tokenizer.local.json");
        var mode = "accurate";

        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index];
            if (option is not ("--text" or "--mode" or "--config"))
            {
                return ShowUsageError($"不支持的参数：{option}");
            }

            if (index + 1 >= args.Length)
            {
                return ShowUsageError($"参数 {option} 缺少值。");
            }

            var value = args[++index];
            switch (option)
            {
                case "--text":
                    text = value;
                    break;
                case "--mode":
                    mode = value;
                    break;
                case "--config":
                    configPath = Path.GetFullPath(value);
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return ShowUsageError("请通过 --text 指定需要分词的非空文本。");
        }

        if (mode is not ("accurate" or "search" or "full"))
        {
            return ShowUsageError("--mode 只能是 accurate、search 或 full。");
        }

        Console.WriteLine($"模式：{mode}");
        Console.WriteLine($"配置：{configPath}");
        Console.WriteLine("正在加载词典并预热……");

        Tokenizer tokenizer;
        try
        {
            var config = JsonSerializer.Deserialize<TokenizerConfig>(File.ReadAllText(configPath));
            tokenizer = new Tokenizer(config, Path.GetDirectoryName(configPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Console.Error.WriteLine($"加载分词配置或用户词典失败：{exception.Message}");
            return 1;
        }

        // 用相同文本和模式完整预热，排除模型延迟加载及首次分词调用的初始化开销。
        tokenizer.Tokenize(text, mode);

        // Infrastructure 完整枚举结果后返回，计时包含分词和结果整理。
        var stopwatch = Stopwatch.StartNew();
        var words = tokenizer.Tokenize(text, mode);
        stopwatch.Stop();

        Console.WriteLine($"原文：{text}");
        Console.WriteLine($"结果：{string.Join(" / ", words)}");
        Console.WriteLine($"词元数量：{words.Count}");
        Console.WriteLine($"分词处理耗时：{stopwatch.Elapsed.TotalMilliseconds:F2} ms");
        return 0;
    }

    private static int ShowHelp()
    {
        Console.WriteLine("""
            用法：
              QQAIAgent.Cli.exe tokenize --text "待分词文本" [--mode accurate|search|full]
                  [--config "分词配置路径"]

            参数：
              --text         待分词文本，包含空格时请用引号包围
              --mode         accurate 精确模式（默认）、search 搜索模式、full 全模式
              --config       默认读取程序目录下 Config/tokenizer.local.json
              --help、-h     显示帮助

            配置中的 UserDictionaryPaths 数组指定用户词典，空数组只使用内置词典。
            词典相对路径以配置文件所在目录为基准；--config 相对路径按当前工作目录解析。
            原始文本直接参与分词，结果忽略空白词元。
            每次执行先加载词典并预热，再计时分词和结果整理。

            示例：
              QQAIAgent.Cli.exe tokenize --text "小明毕业于中国科学院计算所" --mode search
              QQAIAgent.Cli.exe tokenize --text "原神攻略 https://example.com 配队" --mode search
              QQAIAgent.Cli.exe tokenize --text "测试自定义游戏术语" --config "D:\Bot\tokenizer.local.json"
            """);
        return 0;
    }

    private static int ShowUsageError(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine("使用 QQAIAgent.Cli.exe tokenize --help 查看帮助。");
        return 2;
    }
    #endregion
}
