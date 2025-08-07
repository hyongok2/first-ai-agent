using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;

namespace McpAgent.Infrastructure.MCP;

public class MockMcpClientAdapter : IMcpClientAdapter
{
    public Task<IReadOnlyList<ToolDefinition>> GetAvailableToolsAsync(CancellationToken cancellationToken = default)
    {
        var tools = new List<ToolDefinition>
        {
            new ToolDefinition
            {
                Name = "echo",
                Description = "Echo back the input text",
                Parameters = new Dictionary<string, ParameterDefinition>
                {
                    ["text"] = new ParameterDefinition
                    {
                        Type = "string",
                        Description = "Text to echo back",
                        Required = true
                    }
                }
            },
            new ToolDefinition
            {
                Name = "get_time",
                Description = "Get current time",
                Parameters = new Dictionary<string, ParameterDefinition>()
            }
        };

        return Task.FromResult<IReadOnlyList<ToolDefinition>>(tools.AsReadOnly());
    }

    public Task<object?> CallToolAsync(string toolName, Dictionary<string, object> arguments, CancellationToken cancellationToken = default)
    {
        return toolName.ToLowerInvariant() switch
        {
            "echo" => Task.FromResult<object?>(new { 
                result = arguments.TryGetValue("text", out var text) ? text?.ToString() : "Hello from echo!" 
            }),
            "get_time" => Task.FromResult<object?>(new { 
                current_time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                timezone = TimeZoneInfo.Local.DisplayName
            }),
            _ => Task.FromResult<object?>(new { error = $"Unknown tool: {toolName}" })
        };
    }

    public Task<IReadOnlyList<string>> GetConnectedServersAsync()
    {
        var servers = new[] { "mock-server" };
        return Task.FromResult<IReadOnlyList<string>>(servers.AsReadOnly());
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}