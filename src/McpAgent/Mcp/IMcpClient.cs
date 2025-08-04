using McpAgent.Models;
using McpAgent.Providers;

namespace McpAgent.Mcp;

public interface IMcpClient
{
    Task<bool> ConnectAsync(string serverName, CancellationToken cancellationToken = default);
    Task DisconnectAsync(string serverName, CancellationToken cancellationToken = default);
    Task<List<ToolDefinition>> GetAvailableToolsAsync(CancellationToken cancellationToken = default);
    Task<object?> CallToolAsync(string toolName, Dictionary<string, object> arguments, CancellationToken cancellationToken = default);
    Task<List<string>> GetConnectedServersAsync();
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}