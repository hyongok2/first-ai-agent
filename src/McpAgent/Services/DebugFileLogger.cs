using System.Text;

namespace McpAgent.Services;

public class DebugFileLogger : IDebugFileLogger
{
    private readonly string _debugDirectory;

    public DebugFileLogger()
    {
        _debugDirectory = Path.Combine(AppContext.BaseDirectory, "debug-logs");
        Directory.CreateDirectory(_debugDirectory);
    }

    public async Task LogPromptAndResponseAsync(string request, string response, string category = "prompt")
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        var filename = $"{timestamp}_{category}_prompt.txt";
        var filepath = Path.Combine(_debugDirectory, filename);

        var content = new StringBuilder();
        content.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        content.AppendLine($"Category: {category}");
        content.AppendLine();
        content.AppendLine("=== REQUEST ===");
        content.AppendLine(request);
        content.AppendLine();
        content.AppendLine("=== RESPONSE ===");
        content.AppendLine(response);
        content.AppendLine();

        await File.WriteAllTextAsync(filepath, content.ToString(), Encoding.UTF8);
    }

    public async Task LogMcpRequestAndResponseAsync(string request, string response, string category = "mcp")
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        var filename = $"{timestamp}_{category}_request.txt";
        var filepath = Path.Combine(_debugDirectory, filename);

        var content = new StringBuilder();
        content.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        content.AppendLine($"Category: {category}");
        content.AppendLine();
        content.AppendLine("=== MCP REQUEST ===");
        content.AppendLine(request);
        content.AppendLine();
        content.AppendLine("=== MCP RESPONSE ===");
        content.AppendLine(response);
        content.AppendLine();

        await File.WriteAllTextAsync(filepath, content.ToString(), Encoding.UTF8);
    }
}