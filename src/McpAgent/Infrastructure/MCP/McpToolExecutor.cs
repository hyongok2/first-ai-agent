using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace McpAgent.Infrastructure.MCP;

public class McpToolExecutor : IToolExecutor
{
    private readonly ILogger<McpToolExecutor> _logger;
    private readonly IMcpClientAdapter _mcpClient;

    public McpToolExecutor(ILogger<McpToolExecutor> logger, IMcpClientAdapter mcpClient)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mcpClient = mcpClient ?? throw new ArgumentNullException(nameof(mcpClient));
    }

    public async Task<ToolCall> ExecuteAsync(string toolName, Dictionary<string, object> arguments, CancellationToken cancellationToken = default)
    {
        var toolCall = new ToolCall(toolName, arguments);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("Executing tool {ToolName} with {ArgumentCount} arguments", toolName, arguments.Count);

            var result = await _mcpClient.CallToolAsync(toolName, arguments, cancellationToken);
            stopwatch.Stop();

            toolCall.SetResult(result ?? new { success = true }, stopwatch.Elapsed);
            _logger.LogInformation("Tool {ToolName} executed successfully in {Duration}ms", toolName, stopwatch.ElapsedMilliseconds);

            return toolCall;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Tool {ToolName} execution failed after {Duration}ms", toolName, stopwatch.ElapsedMilliseconds);

            toolCall.SetError(ex.Message, stopwatch.Elapsed);
            return toolCall;
        }
    }

    public async Task<IReadOnlyList<ToolDefinition>> GetAvailableToolsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _mcpClient.GetAvailableToolsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get available tools");
            return Array.Empty<ToolDefinition>();
        }
    }

    public async Task<bool> IsToolAvailableAsync(string toolName, CancellationToken cancellationToken = default)
    {
        try
        {
            var availableTools = await GetAvailableToolsAsync(cancellationToken);
            return availableTools.Any(t => t.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check tool availability for {ToolName}", toolName);
            return false;
        }
    }
}

