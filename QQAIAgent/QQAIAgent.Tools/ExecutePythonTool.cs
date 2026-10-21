using System.Diagnostics;
using System.Text;
using System.Text.Json;
using QQAIAgent.Abstractions.Tool;
using QQAIAgent.Tools.Config;

namespace QQAIAgent.Tools;

public sealed class ExecutePythonTool : ITool, IDisposable
{
    #region fields
    // 工具为单例，串行执行以限制同时运行的解释器数量。
    private readonly SemaphoreSlim _executionLock = new(1, 1);
    #endregion

    #region properties
    public string Name => "execute_python";
    #endregion

    #region methods
    public async Task<ToolResult> ExecuteAsync(string argumentsJson, ToolContext context, CancellationToken cancellationToken)
    {
        using var arguments = JsonDocument.Parse(argumentsJson);
        var script = arguments.RootElement.GetProperty("script").GetString();
        ArgumentException.ThrowIfNullOrWhiteSpace(script);

        await _executionLock.WaitAsync(cancellationToken);
        try
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "Config", "python.local.json");
            var config = JsonSerializer.Deserialize<PythonConfig>(await File.ReadAllTextAsync(configPath, cancellationToken))
                ?? throw new InvalidDataException($"Python 配置不能为 null：{configPath}");
            var encoding = new UTF8Encoding(false);
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.GetFullPath(config.ExecutablePath, AppContext.BaseDirectory),
                // 从标准输入读取源码，不生成脚本文件或字节码缓存；这些选项不提供安全沙箱。
                Arguments = "-B -X utf8 -u -",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = encoding,
                StandardOutputEncoding = encoding,
                StandardErrorEncoding = encoding
            };
            startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
            cancellationToken.ThrowIfCancellationRequested();
            var startedAt = Stopwatch.GetTimestamp();
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("无法启动 Python。");
            try
            {
                // 先读取输出再发送源码，关闭标准输入表示源码传输结束。
                var outputTask = ReadOutputAsync(process.StandardOutput, config.MaxOutputCharacters, cancellationToken);
                var errorTask = ReadOutputAsync(process.StandardError, config.MaxOutputCharacters, cancellationToken);
                await process.StandardInput.WriteAsync(script.AsMemory(), cancellationToken);
                process.StandardInput.Close();
                await Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync(cancellationToken));
                var output = await outputTask;
                var error = await errorTask;
                return new ToolResult(JsonSerializer.Serialize(new
                {
                    exitCode = process.ExitCode,
                    stdout = output.Text,
                    stderr = error.Text,
                    stdoutTruncated = output.Truncated,
                    stderrTruncated = error.Truncated,
                    durationMs = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds
                }));
            }
            finally
            {
                // 取消等待不会终止解释器，必须同时清理仍在运行的进程树。
                if (process.HasExited == false)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }
        }
        finally
        {
            _executionLock.Release();
        }
    }

    private static async Task<(string Text, bool Truncated)> ReadOutputAsync(StreamReader reader, int limit, CancellationToken cancellationToken)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        var truncated = false;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            var retained = Math.Min(count, Math.Max(0, limit - text.Length));
            text.Append(buffer, 0, retained);
            truncated |= retained < count;
            // 达到保留上限后仍排空管道，避免解释器因输出阻塞。
        }

        return (text.ToString(), truncated);
    }

    public void Dispose()
    {
        _executionLock.Dispose();
    }
    #endregion
}
