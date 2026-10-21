using System.Reflection;

return args switch
{
    [] or ["help"] or ["--help"] or ["-h"] => ShowHelp(),
    ["version"] or ["--version"] or ["-v"] => ShowVersion(),
    _ => ShowUsageError(args)
};

static int ShowHelp()
{
    Console.WriteLine("""
        QQAIAgent 独立命令行工具

        用法：
          QQAIAgent.Cli.exe <命令> [参数]

        命令：
          help       显示帮助（--help、-h）
          version    显示版本（--version、-v）

        不指定命令时显示帮助。当前尚未接入业务命令。

        退出码：
          0    执行成功
          2    命令或参数错误
        """);
    return 0;
}

static int ShowVersion()
{
    Console.WriteLine($"QQAIAgent.Cli {Assembly.GetExecutingAssembly().GetName().Version}");
    return 0;
}

static int ShowUsageError(string[] arguments)
{
    Console.Error.WriteLine($"无法识别命令或参数：{string.Join(" ", arguments)}");
    Console.Error.WriteLine("使用 QQAIAgent.Cli.exe --help 查看帮助。");
    return 2;
}
