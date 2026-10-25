using System.Reflection;
using QQAIAgent.Cli.Commands;

return args switch
{
    [] or ["help"] or ["--help"] or ["-h"] => ShowHelp(),
    ["version"] or ["--version"] or ["-v"] => ShowVersion(),
    ["tokenize", .. var commandArguments] => TokenizeCommand.Execute(commandArguments),
    ["fts", .. var commandArguments] => await FtsCommand.ExecuteAsync(commandArguments),
    ["embedding", .. var commandArguments] => EmbeddingCommand.Execute(commandArguments),
    ["vector", .. var commandArguments] => await VectorCommand.ExecuteAsync(commandArguments),
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
          tokenize   测试 Jieba 分词（tokenize --help 查看参数）
          fts        重建或搜索群消息全文索引（fts --help 查看参数）
          embedding  测试单句向量生成（embedding --help 查看参数）
          vector     全量重建群消息向量（vector --help 查看参数）

        不指定命令时显示帮助。

        退出码：
          0    执行成功
          1    执行失败
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
