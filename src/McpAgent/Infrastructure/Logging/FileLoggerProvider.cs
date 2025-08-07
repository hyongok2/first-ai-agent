using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace McpAgent.Infrastructure.Logging;

/// <summary>
/// 파일 로거 프로바이더
/// </summary>
public class FileLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();
    private readonly string _logDirectory;
    private bool _disposed = false;

    public FileLoggerProvider()
    {
        _logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Logs");
        Directory.CreateDirectory(_logDirectory);
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _logDirectory));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            foreach (var logger in _loggers.Values)
            {
                logger.Dispose();
            }
            _loggers.Clear();
            _disposed = true;
        }
    }
}

/// <summary>
/// 파일 로거 구현
/// </summary>
public class FileLogger : ILogger, IDisposable
{
    private readonly string _categoryName;
    private readonly string _logFilePath;
    private readonly object _lock = new();
    private bool _disposed = false;

    public FileLogger(string categoryName, string logDirectory)
    {
        _categoryName = categoryName;
        var fileName = $"system-{DateTime.Now:yyyy-MM-dd}.log";
        _logFilePath = Path.Combine(logDirectory, fileName);
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel) || _disposed)
            return;

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logLevelString = GetLogLevelString(logLevel);
        var message = formatter(state, exception);

        if (exception != null)
        {
            message += $" | Exception: {exception.Message}";
        }

        var logLine = $"{timestamp} [{logLevelString}] {_categoryName}: {message}";

        lock (_lock)
        {
            try
            {
                File.AppendAllText(_logFilePath, logLine + Environment.NewLine);
            }
            catch
            {
                // 파일 쓰기 실패시 조용히 무시
            }
        }
    }

    private static string GetLogLevelString(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => "TRCE",
            LogLevel.Debug => "DBUG",
            LogLevel.Information => "INFO",
            LogLevel.Warning => "WARN",
            LogLevel.Error => "ERRO",
            LogLevel.Critical => "CRIT",
            LogLevel.None => "NONE",
            _ => "UNKN"
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}