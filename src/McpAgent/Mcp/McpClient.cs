using System.Diagnostics;
using System.Text.Json;
using McpAgent.Configuration;
using McpAgent.Models;
using McpAgent.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace McpAgent.Mcp;

public class McpClient : IMcpClient
{
    private readonly ILogger<McpClient> _logger;
    private readonly McpConfiguration _config;
    private readonly Dictionary<string, McpServerConnection> _connections = new();

    public McpClient(ILogger<McpClient> logger, IOptions<AgentConfiguration> options)
    {
        _logger = logger;
        _config = options.Value.Mcp;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!_config.Enabled)
        {
            _logger.LogInformation("MCP is disabled in configuration");
            return;
        }

        _logger.LogInformation("Initializing MCP client with {ServerCount} servers", _config.Servers.Count);

        foreach (var serverConfig in _config.Servers)
        {
            try
            {
                await ConnectAsync(serverConfig.Name, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to MCP server {ServerName}", serverConfig.Name);
            }
        }
    }

    public async Task<bool> ConnectAsync(string serverName, CancellationToken cancellationToken = default)
    {
        var serverConfig = _config.Servers.FirstOrDefault(s => s.Name == serverName);
        if (serverConfig == null)
        {
            _logger.LogError("Server configuration not found for {ServerName}", serverName);
            return false;
        }

        try
        {
            _logger.LogInformation("Connecting to MCP server {ServerName}", serverName);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = serverConfig.Command,
                    Arguments = string.Join(" ", serverConfig.Args),
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            foreach (var env in serverConfig.Env)
            {
                process.StartInfo.Environment[env.Key] = env.Value;
            }

            process.Start();

            var connection = new McpServerConnection(serverName, process, _logger);
            await connection.InitializeAsync(cancellationToken);

            _connections[serverName] = connection;
            _logger.LogInformation("Successfully connected to MCP server {ServerName}", serverName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to MCP server {ServerName}", serverName);
            return false;
        }
    }

    public async Task DisconnectAsync(string serverName, CancellationToken cancellationToken = default)
    {
        if (_connections.TryGetValue(serverName, out var connection))
        {
            await connection.DisposeAsync();
            _connections.Remove(serverName);
            _logger.LogInformation("Disconnected from MCP server {ServerName}", serverName);
        }
    }

    public async Task<List<ToolDefinition>> GetAvailableToolsAsync(CancellationToken cancellationToken = default)
    {
        var allTools = new List<ToolDefinition>();

        foreach (var connection in _connections.Values)
        {
            try
            {
                var tools = await connection.GetToolsAsync(cancellationToken);
                allTools.AddRange(tools);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get tools from server {ServerName}", connection.ServerName);
            }
        }

        return allTools;
    }

    public async Task<object?> CallToolAsync(string toolName, Dictionary<string, object> arguments, CancellationToken cancellationToken = default)
    {
        foreach (var connection in _connections.Values)
        {
            try
            {
                var tools = await connection.GetToolsAsync(cancellationToken);
                if (tools.Any(t => t.Name == toolName))
                {
                    return await connection.CallToolAsync(toolName, arguments, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling tool {ToolName} on server {ServerName}", toolName, connection.ServerName);
            }
        }

        throw new InvalidOperationException($"Tool '{toolName}' not found on any connected server");
    }

    public Task<List<string>> GetConnectedServersAsync()
    {
        return Task.FromResult(_connections.Keys.ToList());
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        var disconnectTasks = _connections.Keys.Select(serverName => DisconnectAsync(serverName, cancellationToken));
        await Task.WhenAll(disconnectTasks);
    }
}

internal class McpServerConnection : IAsyncDisposable
{
    private readonly string _serverName;
    private readonly Process _process;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _requestSemaphore = new(1, 1);

    public string ServerName => _serverName;

    public McpServerConnection(string serverName, Process process, ILogger logger)
    {
        _serverName = serverName;
        _process = process;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var initRequest = new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new
                {
                    name = "McpAgent",
                    version = "1.0.0"
                }
            }
        };

        await SendRequestAsync(initRequest, cancellationToken);
    }

    public async Task<List<ToolDefinition>> GetToolsAsync(CancellationToken cancellationToken = default)
    {
        var request = new
        {
            jsonrpc = "2.0",
            id = 2,
            method = "tools/list"
        };

        var response = await SendRequestAsync(request, cancellationToken);
        return ParseToolsResponse(response);
    }

    public async Task<object?> CallToolAsync(string toolName, Dictionary<string, object> arguments, CancellationToken cancellationToken = default)
    {
        var request = new
        {
            jsonrpc = "2.0",
            id = 3,
            method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments
            }
        };

        return await SendRequestAsync(request, cancellationToken);
    }

    private async Task<object?> SendRequestAsync(object request, CancellationToken cancellationToken = default)
    {
        await _requestSemaphore.WaitAsync(cancellationToken);
        try
        {
            var json = JsonSerializer.Serialize(request);
            _logger.LogDebug("Sending MCP request to {ServerName}: {Json}", _serverName, json);
            
            // MCP 서버는 별도 프로세스의 stdin/stdout 사용 (콘솔과 분리됨)
            await _process.StandardInput.WriteLineAsync(json);
            await _process.StandardInput.FlushAsync();

            var response = await _process.StandardOutput.ReadLineAsync();
            if (response != null)
            {
                _logger.LogDebug("Received MCP response from {ServerName}: {Response}", _serverName, response);
                return JsonSerializer.Deserialize<object>(response);
            }
            
            _logger.LogWarning("No response received from MCP server {ServerName}", _serverName);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error communicating with MCP server {ServerName}", _serverName);
            throw;
        }
        finally
        {
            _requestSemaphore.Release();
        }
    }

    private List<ToolDefinition> ParseToolsResponse(object? response)
    {
        try
        {
            if (response is JsonElement element && element.TryGetProperty("result", out var result))
            {
                if (result.TryGetProperty("tools", out var tools))
                {
                    var toolList = new List<ToolDefinition>();
                    foreach (var tool in tools.EnumerateArray())
                    {
                        var toolDef = new ToolDefinition();
                        if (tool.TryGetProperty("name", out var name))
                            toolDef.Name = name.GetString() ?? string.Empty;
                        if (tool.TryGetProperty("description", out var desc))
                            toolDef.Description = desc.GetString() ?? string.Empty;
                        
                        toolList.Add(toolDef);
                    }
                    return toolList;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse tools response from {ServerName}", _serverName);
        }

        return new List<ToolDefinition>();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill();
                await _process.WaitForExitAsync();
            }
            _process.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing MCP server connection {ServerName}", _serverName);
        }
        finally
        {
            _requestSemaphore.Dispose();
        }
    }
}