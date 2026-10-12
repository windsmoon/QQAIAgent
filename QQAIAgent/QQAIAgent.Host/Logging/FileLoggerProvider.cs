using System.Text;

namespace QQAIAgent.Host.Logging;

internal sealed class FileLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    #region fields
    private readonly object _sync = new();
    private readonly StreamWriter _writer;
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();
    private bool _disposed;
    #endregion

    #region constructors
    public FileLoggerProvider(string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? AppContext.BaseDirectory);
        _writer = new StreamWriter(filePath, append: true, new UTF8Encoding(false))
        {
            AutoFlush = true
        };
    }
    #endregion

    #region methods
    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(this, categoryName);
    }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider;
    }

    private void Write(string categoryName, LogLevel logLevel, EventId eventId, string message, Exception exception)
    {
        var entry = new StringBuilder()
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
            .Append(" [").Append(logLevel).Append("] ")
            .Append(categoryName).Append('[').Append(eventId.Id).Append(']');
        // 使用宿主共享的作用域，保留请求、模型轮次和工具调用上下文。
        _scopeProvider.ForEachScope(static (scope, builder) => builder.Append(" => ").Append(scope), entry);
        entry.AppendLine().Append(message);
        if (exception is not null)
        {
            entry.AppendLine().Append(exception);
        }

        // 多个日志分类共用文件，整条日志加锁写入，避免并发消息互相穿插。
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _writer.WriteLine(entry.ToString());
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer.Dispose();
        }
    }
    #endregion

    #region nested types
    private sealed class FileLogger(FileLoggerProvider provider, string categoryName) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return provider._scopeProvider.Push(state);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel) == false)
            {
                return;
            }

            provider.Write(categoryName, logLevel, eventId, formatter(state, exception), exception);
        }
    }
    #endregion
}
