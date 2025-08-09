using McpAgent.Application.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace McpAgent.Infrastructure.Logging;

/// <summary>
/// 요청/응답 파일 로거 구현
/// </summary>
public class FileRequestResponseLogger : IRequestResponseLogger
{
    private readonly ILogger<FileRequestResponseLogger> _logger;
    private readonly string _logDirectory;
    private readonly object _fileLock = new();

    private string Seperator(string s) => $"==================== {s} ====================";

    public FileRequestResponseLogger(ILogger<FileRequestResponseLogger> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "RequestResponse");

        // 로그 디렉터리 생성
        Directory.CreateDirectory(_logDirectory);
    }

    public async Task LogLlmRequestResponseAsync(string model, string stage, string request, string response, double elapsedMilliseconds, CancellationToken cancellationToken = default)
    {
        DateTime now = DateTime.Now;
        var dateStr = now.ToString("yyyy-MM-dd-HH-mm-ss-fff");
        var safeModel = model.Replace(":", "-");// Windows에서 콜론은 파일명에 사용할 수 없으므로 대체
        var fileName = $"{dateStr}-{safeModel}-{stage}.log";
        var filePath = Path.Combine(_logDirectory, now.ToString("yyyy-MM-dd"), fileName);

        var content = new StringBuilder();
        content.AppendLine(Seperator("LLM REQUEST/RESPONSE LOG"));
        content.AppendLine($"Timestamp: {now:yyyy-MM-dd HH:mm:ss.fff}");
        content.AppendLine($"Model: {model}");
        content.AppendLine($"Stage: {stage}");
        content.AppendLine($"Elapsed (ms): {elapsedMilliseconds}");
        content.AppendLine();
        content.AppendLine(Seperator("REQUEST"));
        content.AppendLine(TryFormatJson(request));
        content.AppendLine();
        content.AppendLine(Seperator("RESPONSE"));
        content.AppendLine(TryFormatJson(response));
        content.AppendLine();
        content.AppendLine(Seperator("END LOG"));

        await WriteToFileAsync(filePath, content.ToString(), cancellationToken);
    }

    public async Task LogMcpRequestResponseAsync(string mcpServer, string toolName, string request, string response, double elapsedMilliseconds, CancellationToken cancellationToken = default)
    {
        DateTime now = DateTime.Now;
        var dateStr = now.ToString("yyyy-MM-dd-HH-mm-ss-fff");
        var fileName = $"{dateStr}-{mcpServer}-{toolName}.log";
        var filePath = Path.Combine(_logDirectory, now.ToString("yyyy-MM-dd"), fileName);

        var content = new StringBuilder();
        content.AppendLine(Seperator("MCP REQUEST/RESPONSE LOG"));
        content.AppendLine($"Timestamp: {now:yyyy-MM-dd HH:mm:ss.fff}");
        content.AppendLine($"MCP Server: {mcpServer}");
        content.AppendLine($"Tool Name: {toolName}");
        content.AppendLine($"Elapsed (ms): {elapsedMilliseconds}");
        content.AppendLine();
        content.AppendLine(Seperator("REQUEST"));
        content.AppendLine(TryFormatJson(request));
        content.AppendLine();
        content.AppendLine(Seperator("RESPONSE"));
        content.AppendLine(TryFormatJson(response));
        content.AppendLine();
        content.AppendLine(Seperator("END LOG"));

        await WriteToFileAsync(filePath, content.ToString(), cancellationToken);
    }

    /// <summary>
    /// JSON 문자열을 보기 좋게 포맷팅 시도. 실패하면 원본 반환
    /// </summary>
    private string TryFormatJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return json;

        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping // 유니코드 문자 그대로 표시
            });
        }
        catch
        {
            // JSON 파싱 실패 시 원본 반환
            return json;
        }
    }

    private async Task WriteToFileAsync(string filePath, string content, CancellationToken cancellationToken = default)
    {
        try
        {
            // 디렉터리 생성
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(filePath, content, Encoding.UTF8, cancellationToken);

            _logger.LogDebug("Request/Response logged to file: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write request/response log to file: {FilePath}", filePath);
        }
    }
}