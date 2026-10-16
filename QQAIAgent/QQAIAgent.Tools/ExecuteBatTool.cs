using System.Diagnostics;
using System.Text;
using System.Text.Json;
using QQAIAgent.Abstractions.Tool;
using QQAIAgent.Tools.Config;

namespace QQAIAgent.Tools;

public sealed class ExecuteBatTool : ITool, IDisposable
{
    #region fields
    // 工具由宿主注册为单例，所有群串行执行，避免共享文件被同时修改。
    private readonly SemaphoreSlim _executionLock = new(1, 1);
    #endregion

    #region properties
    public string Name => "execute_bat";
    #endregion

    #region methods
    public async Task<string> ExecuteAsync(string argumentsJson, ToolContext context, CancellationToken cancellationToken)
    {
        using var arguments = JsonDocument.Parse(argumentsJson);
        var script = arguments.RootElement.GetProperty("script").GetString();
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        var relativeDirectory = arguments.RootElement.TryGetProperty("working_directory", out var directory)
            ? directory.GetString() ?? "." : ".";

        await _executionLock.WaitAsync(cancellationToken);
        try
        {
            var configPath = Path.Combine(AppContext.BaseDirectory, "Config", "bat.local.json");
            var config = JsonSerializer.Deserialize<BatConfig>(await File.ReadAllTextAsync(configPath, cancellationToken))
                ?? throw new InvalidDataException($"bat 配置不能为 null：{configPath}");
            var rootDirectory = Path.GetFullPath(config.RootDirectory, AppContext.BaseDirectory);
            var groupDirectory = Path.Combine(rootDirectory, "Groups", context.GroupId.ToString());
            var workingDirectory = Path.GetFullPath(relativeDirectory, groupDirectory);
            var relativePath = Path.GetRelativePath(groupDirectory, workingDirectory);
            if (Path.IsPathRooted(relativeDirectory) || relativePath == ".." || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}"))
            {
                throw new ArgumentException("working_directory 必须是当前群工作目录内的相对路径。");
            }

            Directory.CreateDirectory(workingDirectory);
            var runsDirectory = Directory.CreateDirectory(Path.Combine(rootDirectory, "Runs")).FullName;
            var scriptPath = Path.Combine(runsDirectory, $"{Guid.NewGuid():N}.bat");
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var encoding = Encoding.GetEncoding(config.CodePage);
            try
            {
                // 先用 ASCII 命令切换代码页；直接写字节以免 BOM 被 cmd 当作命令内容。
                var content = $"@echo off\r\nchcp {config.CodePage} >nul\r\n{script.ReplaceLineEndings("\r\n")}\r\n";
                await File.WriteAllBytesAsync(scriptPath, encoding.GetBytes(content), cancellationToken);
                var startInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                    Arguments = $"/d /q /s /c \"\"{scriptPath}\"\"",
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = encoding,
                    StandardErrorEncoding = encoding
                };
                cancellationToken.ThrowIfCancellationRequested();
                var startedAt = Stopwatch.GetTimestamp();
                using var process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("无法启动 cmd.exe。");
                try
                {
                    process.StandardInput.Close();
                    var outputTask = ReadOutputAsync(process.StandardOutput, config.MaxOutputCharacters, cancellationToken);
                    var errorTask = ReadOutputAsync(process.StandardError, config.MaxOutputCharacters, cancellationToken);
                    await Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync(cancellationToken));
                    var output = await outputTask;
                    var error = await errorTask;
                    return JsonSerializer.Serialize(new
                    {
                        exitCode = process.ExitCode,
                        stdout = output.Text,
                        stderr = error.Text,
                        stdoutTruncated = output.Truncated,
                        stderrTruncated = error.Truncated,
                        durationMs = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds
                    });
                }
                finally
                {
                    // 取消等待不会终止脚本，必须同时清理仍在运行的进程树。
                    if (process.HasExited == false)
                    {
                        process.Kill(entireProcessTree: true);
                        await process.WaitForExitAsync(CancellationToken.None);
                    }
                }
            }
            finally
            {
                File.Delete(scriptPath);
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
            // 达到保留上限后仍排空管道，避免子进程因输出阻塞。
        }

        return (text.ToString(), truncated);
    }

    public void Dispose()
    {
        _executionLock.Dispose();
    }
    #endregion
}
