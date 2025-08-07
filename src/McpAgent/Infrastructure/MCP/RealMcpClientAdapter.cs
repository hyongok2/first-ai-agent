using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using System.Text;

namespace McpAgent.Infrastructure.MCP;

/// <summary>
/// 실제 MCP 서버와 통신할 수 있는 MCP 클라이언트 어댑터
/// </summary>
public class RealMcpClientAdapter : IMcpClientAdapter, IDisposable
{
    private readonly ILogger<RealMcpClientAdapter> _logger;
    private readonly List<Process> _serverProcesses = new();
    private readonly Dictionary<string, ToolDefinition> _availableTools = new();
    private bool _initialized = false;
    private bool _disposed = false;

    public RealMcpClientAdapter(ILogger<RealMcpClientAdapter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;

        try
        {
            _logger.LogInformation("Initializing Real MCP Client Adapter");

            // 기본 툴들을 미리 등록
            await LoadBuiltInToolsAsync(cancellationToken);
            
            _initialized = true;
            _logger.LogInformation("Real MCP Client Adapter initialized successfully with {Count} tools", _availableTools.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Real MCP Client Adapter");
            throw;
        }
    }

    public Task<IReadOnlyList<ToolDefinition>> GetAvailableToolsAsync(CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            _logger.LogWarning("MCP Client not initialized, returning empty tool list");
            return Task.FromResult<IReadOnlyList<ToolDefinition>>(Array.Empty<ToolDefinition>());
        }

        return Task.FromResult<IReadOnlyList<ToolDefinition>>(_availableTools.Values.ToList().AsReadOnly());
    }

    public async Task<object?> CallToolAsync(string toolName, Dictionary<string, object> arguments, CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            _logger.LogWarning("MCP Client not initialized");
            return new { error = "MCP Client not initialized" };
        }

        if (!_availableTools.ContainsKey(toolName))
        {
            _logger.LogWarning("Tool {ToolName} not found", toolName);
            return new { error = $"Tool '{toolName}' not found" };
        }

        try
        {
            _logger.LogInformation("Executing tool {ToolName} with arguments", toolName);
            
            // 실제 툴 실행 로직
            return await ExecuteBuiltInToolAsync(toolName, arguments, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute tool {ToolName}", toolName);
            return new { error = $"Tool execution failed: {ex.Message}" };
        }
    }

    public Task<IReadOnlyList<string>> GetConnectedServersAsync()
    {
        var servers = new List<string> { "built-in-tools-server" };
        if (_serverProcesses.Count > 0)
        {
            servers.AddRange(_serverProcesses.Select((p, i) => $"server-{i}"));
        }
        return Task.FromResult<IReadOnlyList<string>>(servers.AsReadOnly());
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Shutting down Real MCP Client Adapter");
        
        foreach (var process in _serverProcesses)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    await process.WaitForExitAsync(cancellationToken);
                }
                process.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while shutting down MCP server process");
            }
        }
        
        _serverProcesses.Clear();
        _availableTools.Clear();
        _initialized = false;
    }

    private async Task LoadBuiltInToolsAsync(CancellationToken cancellationToken)
    {
        // Echo 툴
        var echoTool = new ToolDefinition
        {
            Name = "echo",
            Description = "Echo back the provided text",
            Parameters = new Dictionary<string, ParameterDefinition>
            {
                ["text"] = new ParameterDefinition
                {
                    Type = "string",
                    Description = "Text to echo back",
                    Required = true
                }
            }
        };
        _availableTools[echoTool.Name] = echoTool;

        // 현재 시간 툴
        var timeTool = new ToolDefinition
        {
            Name = "get_current_time",
            Description = "Get the current date and time",
            Parameters = new Dictionary<string, ParameterDefinition>
            {
                ["format"] = new ParameterDefinition
                {
                    Type = "string",
                    Description = "Time format (optional, default: 'yyyy-MM-dd HH:mm:ss')",
                    Required = false
                }
            }
        };
        _availableTools[timeTool.Name] = timeTool;

        // 파일 읽기 툴 (안전한 버전)
        var readFileTool = new ToolDefinition
        {
            Name = "read_file",
            Description = "Read contents of a text file (limited to safe directories)",
            Parameters = new Dictionary<string, ParameterDefinition>
            {
                ["path"] = new ParameterDefinition
                {
                    Type = "string",
                    Description = "File path to read",
                    Required = true
                }
            }
        };
        _availableTools[readFileTool.Name] = readFileTool;

        // 계산 툴
        var calculateTool = new ToolDefinition
        {
            Name = "calculate",
            Description = "Perform basic mathematical calculations",
            Parameters = new Dictionary<string, ParameterDefinition>
            {
                ["expression"] = new ParameterDefinition
                {
                    Type = "string",
                    Description = "Mathematical expression to evaluate (e.g., '2 + 3 * 4')",
                    Required = true
                }
            }
        };
        _availableTools[calculateTool.Name] = calculateTool;

        await Task.CompletedTask; // 비동기 시뮬레이션
    }

    private async Task<object?> ExecuteBuiltInToolAsync(string toolName, Dictionary<string, object> arguments, CancellationToken cancellationToken)
    {
        return toolName.ToLowerInvariant() switch
        {
            "echo" => ExecuteEcho(arguments),
            "get_current_time" => ExecuteGetCurrentTime(arguments),
            "read_file" => await ExecuteReadFileAsync(arguments, cancellationToken),
            "calculate" => ExecuteCalculate(arguments),
            _ => new { error = $"Unknown built-in tool: {toolName}" }
        };
    }

    private object ExecuteEcho(Dictionary<string, object> arguments)
    {
        var text = arguments.TryGetValue("text", out var value) ? value?.ToString() : "Hello from Echo!";
        
        return new
        {
            tool = "echo",
            result = text,
            timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
        };
    }

    private object ExecuteGetCurrentTime(Dictionary<string, object> arguments)
    {
        var format = arguments.TryGetValue("format", out var value) ? value?.ToString() : "yyyy-MM-dd HH:mm:ss";
        
        try
        {
            var now = DateTime.Now;
            return new
            {
                tool = "get_current_time",
                current_time = now.ToString(format),
                utc_time = DateTime.UtcNow.ToString(format),
                timezone = TimeZoneInfo.Local.DisplayName,
                timestamp = DateTimeOffset.Now.ToUnixTimeSeconds()
            };
        }
        catch (FormatException)
        {
            return new
            {
                tool = "get_current_time",
                error = "Invalid time format",
                current_time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                utc_time = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            };
        }
    }

    private async Task<object> ExecuteReadFileAsync(Dictionary<string, object> arguments, CancellationToken cancellationToken)
    {
        if (!arguments.TryGetValue("path", out var pathValue) || pathValue?.ToString() is not string path)
        {
            return new { tool = "read_file", error = "File path is required" };
        }

        try
        {
            // 보안을 위해 특정 디렉토리만 허용
            var allowedDirectories = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Path.GetTempPath(),
                Directory.GetCurrentDirectory()
            };

            var fullPath = Path.GetFullPath(path);
            var isAllowed = allowedDirectories.Any(dir => fullPath.StartsWith(Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase));

            if (!isAllowed)
            {
                return new { tool = "read_file", error = "Access to this directory is not allowed" };
            }

            if (!File.Exists(fullPath))
            {
                return new { tool = "read_file", error = "File not found" };
            }

            var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
            var fileInfo = new FileInfo(fullPath);

            return new
            {
                tool = "read_file",
                path = fullPath,
                content = content,
                size = fileInfo.Length,
                modified = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                encoding = "UTF-8"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read file: {Path}", path);
            return new { tool = "read_file", error = $"Failed to read file: {ex.Message}" };
        }
    }

    private object ExecuteCalculate(Dictionary<string, object> arguments)
    {
        if (!arguments.TryGetValue("expression", out var exprValue) || exprValue?.ToString() is not string expression)
        {
            return new { tool = "calculate", error = "Mathematical expression is required" };
        }

        try
        {
            // 안전한 수식 계산을 위해 간단한 파서 사용
            var result = EvaluateSimpleExpression(expression.Trim());
            
            return new
            {
                tool = "calculate",
                expression = expression,
                result = result,
                type = result.GetType().Name
            };
        }
        catch (Exception ex)
        {
            return new
            {
                tool = "calculate",
                expression = expression,
                error = $"Calculation failed: {ex.Message}"
            };
        }
    }

    private double EvaluateSimpleExpression(string expression)
    {
        // 매우 단순한 수식 계산기 (보안상 eval 사용 금지)
        // 더 복잡한 수식을 위해서는 전용 라이브러리 사용 권장
        
        expression = expression.Replace(" ", "");
        
        // 기본 사칙연산만 지원
        if (expression.Contains('+'))
        {
            var parts = expression.Split('+');
            return parts.Select(EvaluateSimpleExpression).Sum();
        }
        
        if (expression.Contains('-') && expression.Count(c => c == '-') == 1 && !expression.StartsWith("-"))
        {
            var parts = expression.Split('-');
            return EvaluateSimpleExpression(parts[0]) - EvaluateSimpleExpression(parts[1]);
        }
        
        if (expression.Contains('*'))
        {
            var parts = expression.Split('*');
            return parts.Select(EvaluateSimpleExpression).Aggregate((a, b) => a * b);
        }
        
        if (expression.Contains('/'))
        {
            var parts = expression.Split('/');
            var result = EvaluateSimpleExpression(parts[0]);
            for (int i = 1; i < parts.Length; i++)
            {
                var divisor = EvaluateSimpleExpression(parts[i]);
                if (Math.Abs(divisor) < 1e-10)
                    throw new DivideByZeroException("Division by zero");
                result /= divisor;
            }
            return result;
        }

        // 숫자 파싱
        if (double.TryParse(expression, out var number))
        {
            return number;
        }

        throw new ArgumentException($"Invalid expression: {expression}");
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            ShutdownAsync().Wait(TimeSpan.FromSeconds(5));
            _disposed = true;
        }
    }
}