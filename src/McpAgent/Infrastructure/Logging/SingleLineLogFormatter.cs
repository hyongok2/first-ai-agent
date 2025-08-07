using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace McpAgent.Infrastructure.Logging;

/// <summary>
/// 단일 라인 로그 포맷터
/// </summary>
public class SingleLineLogFormatter : ConsoleFormatter
{
    public SingleLineLogFormatter() : base("SingleLine")
    {
    }

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logLevel = GetLogLevelString(logEntry.LogLevel);
        var categoryName = logEntry.Category;
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);

        // Exception이 있는 경우 메시지에 추가
        if (logEntry.Exception != null)
        {
            message += $" | Exception: {logEntry.Exception.Message}";
        }

        // 단일 라인으로 포맷팅
        var logLine = $"{timestamp} [{logLevel}] {categoryName}: {message}";
        
        textWriter.WriteLine(logLine);
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
}