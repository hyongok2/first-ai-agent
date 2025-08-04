using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using McpAgent.Configuration;
using McpAgent.Models;
using McpAgent.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace McpAgent.Mcp;

public class TcpMcpClient : IMcpClient
{
    private readonly ILogger<TcpMcpClient> _logger;
    private readonly McpConfiguration _config;
    private readonly Dictionary<string, TcpServerConnection> _connections = new();

    public TcpMcpClient(ILogger<TcpMcpClient> logger, IOptions<AgentConfiguration> options)
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

        _logger.LogInformation("Initializing TCP MCP client");
        
        foreach (var serverConfig in _config.Servers)
        {
            try
            {
                await ConnectAsync(serverConfig.Name, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to TCP MCP server {ServerName}", serverConfig.Name);
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
            _logger.LogInformation("Connecting to TCP MCP server {ServerName}", serverName);

            // For TCP connection, we'd need port configuration
            // This is a conceptual implementation
            var connection = new TcpServerConnection(serverName, _logger);
            await connection.ConnectAsync("localhost", 9000, cancellationToken); // Example port
            
            _connections[serverName] = connection;
            _logger.LogInformation("Successfully connected to TCP MCP server {ServerName}", serverName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to TCP MCP server {ServerName}", serverName);
            return false;
        }
    }

    public async Task DisconnectAsync(string serverName, CancellationToken cancellationToken = default)
    {
        if (_connections.TryGetValue(serverName, out var connection))
        {
            await connection.DisconnectAsync();
            _connections.Remove(serverName);
            _logger.LogInformation("Disconnected from TCP MCP server {ServerName}", serverName);
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
                _logger.LogError(ex, "Failed to get tools from TCP server {ServerName}", connection.ServerName);
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
                _logger.LogError(ex, "Error calling tool {ToolName} on TCP server {ServerName}", toolName, connection.ServerName);
            }
        }

        throw new InvalidOperationException($"Tool '{toolName}' not found on any connected TCP server");
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

internal class TcpServerConnection
{
    private readonly string _serverName;
    private readonly ILogger _logger;
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;

    public string ServerName => _serverName;

    public TcpServerConnection(string serverName, ILogger logger)
    {
        _serverName = serverName;
        _logger = logger;
    }

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(host, port);
        _stream = _tcpClient.GetStream();
    }

    public async Task DisconnectAsync()
    {
        _stream?.Close();
        _tcpClient?.Close();
    }

    public async Task<List<ToolDefinition>> GetToolsAsync(CancellationToken cancellationToken)
    {
        // TCP implementation for getting tools
        return new List<ToolDefinition>();
    }

    public async Task<object?> CallToolAsync(string toolName, Dictionary<string, object> arguments, CancellationToken cancellationToken)
    {
        // TCP implementation for calling tools
        return null;
    }
}