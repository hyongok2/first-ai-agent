using McpAgent.Application.Interfaces;
using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;
using McpAgent.Infrastructure.MCP.Models;
using McpAgent.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text;
using McpAgent.Shared.Utils;

namespace McpAgent.Infrastructure.MCP;

/// <summary>
/// 실제 MCP 프로토콜을 구현한 MCP 클라이언트 어댑터
/// </summary>
public class ProperMcpClientAdapter : IMcpClientAdapter, IDisposable
{
    private readonly ILogger<ProperMcpClientAdapter> _logger;
    private readonly McpConfiguration _config;
    private readonly IRequestResponseLogger _requestResponseLogger;
    private readonly Dictionary<string, McpServerConnection> _connections = new();
    private Timer? _healthCheckTimer;
    private bool _initialized = false;
    private bool _disposed = false;

    // 도구 캐시 (재연결 시에만 무효화)
    private IReadOnlyList<ToolDefinition>? _cachedTools = null;
    private readonly object _cacheLock = new object();

    public ProperMcpClientAdapter(
        ILogger<ProperMcpClientAdapter> logger,
        IOptions<AgentConfiguration> options,
        IRequestResponseLogger requestResponseLogger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = options?.Value?.Mcp ?? throw new ArgumentNullException(nameof(options));
        _requestResponseLogger = requestResponseLogger ?? throw new ArgumentNullException(nameof(requestResponseLogger));

        // Don't start timer here - wait until after initialization
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;

        if (!_config.Enabled)
        {
            _logger.LogInformation("MCP is disabled in configuration");
            _initialized = true;
            return;
        }

        _logger.LogInformation("Initializing MCP client with {ServerCount} servers", _config.Servers.Count);

        foreach (var serverConfig in _config.Servers)
        {
            try
            {
                await ConnectToServerAsync(serverConfig, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to connect to MCP server {ServerName}", serverConfig.Name);
            }
        }

        // Start health check timer only after successful initialization
        // if (_connections.Count > 0)
        // {
        //     var healthCheckInterval = TimeSpan.FromSeconds(_config.HealthCheckIntervalSeconds);
        //     _healthCheckTimer = new Timer(PerformHealthCheck, null, healthCheckInterval, healthCheckInterval);
        //     _logger.LogInformation("Health check timer started with {Interval}s interval", _config.HealthCheckIntervalSeconds);
        // }

        _initialized = true;
        _logger.LogInformation("MCP client initialization completed with {ConnectedCount}/{TotalCount} servers",
            _connections.Count, _config.Servers.Count);

        // 초기화 완료 후 도구 목록을 미리 캐시에 로드
        if (_connections.Count == 0) return;
        try
        {
            _logger.LogInformation("Pre-loading tool list during MCP initialization...");
            await GetAvailableToolsAsync(cancellationToken);
            _logger.LogInformation("Tool list pre-loaded successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pre-load tool list during initialization");
            // 초기화 실패하지 않도록 continue
        }
    }

    public async Task<IReadOnlyList<ToolDefinition>> GetAvailableToolsAsync(CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            _logger.LogWarning("MCP client not initialized, returning empty tool list");
            return Array.Empty<ToolDefinition>();
        }

        // 캐시 확인
        lock (_cacheLock)
        {
            if (_cachedTools != null)
            {
                _logger.LogDebug("Returning cached tools - {ToolCount} tools from cache", _cachedTools.Count);
                return _cachedTools;
            }
        }

        // 캐시가 없는 경우 새로 조회
        _logger.LogDebug("No cached tools available, fetching fresh tool list");
        var allTools = new List<ToolDefinition>();

        foreach (var connection in _connections.Values)
        {
            try
            {
                var mcpTools = await connection.GetToolsAsync(cancellationToken);
                var domainTools = ConvertMcpToolsToDomainTools(mcpTools);
                allTools.AddRange(domainTools);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get tools from server {ServerName}", connection.ServerName);
            }
        }

        var toolList = allTools.AsReadOnly();

        // 캐시 업데이트
        lock (_cacheLock)
        {
            _cachedTools = toolList;
        }

        _logger.LogInformation("Retrieved {ToolCount} tools from {ServerCount} servers and updated cache", toolList.Count, _connections.Count);
        return toolList;
    }

    public async Task<object?> CallToolAsync(string toolName, Dictionary<string, object> arguments, CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            _logger.LogWarning("MCP client not initialized");
            return new { error = "MCP client not initialized" };
        }

        foreach (var connection in _connections.Values)
        {
            try
            {
                var tools = await connection.GetToolsAsync(cancellationToken);
                if (tools.Any(t => t.Name == toolName))
                {
                    _logger.LogInformation("Calling tool {ToolName} on server {ServerName}", toolName, connection.ServerName);
                    var result = await connection.CallToolAsync(toolName, arguments, cancellationToken);

                    _logger.LogInformation("Tool {ToolName} executed successfully on server {ServerName}", toolName, connection.ServerName);
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling tool {ToolName} on server {ServerName}", toolName, connection.ServerName);
            }
        }

        _logger.LogWarning("Tool {ToolName} not found on any connected server", toolName);
        return new { error = $"Tool '{toolName}' not found on any connected server" };
    }

    public Task<IReadOnlyList<string>> GetConnectedServersAsync()
    {
        var connectedServers = _connections.Values
            .Where(c => c.IsConnected)
            .Select(c => c.ServerName)
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(connectedServers.AsReadOnly());
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Shutting down MCP client with {ServerCount} servers", _connections.Count);

        _healthCheckTimer?.Dispose();

        var shutdownTasks = _connections.Values.Select(connection => connection.DisposeAsync().AsTask());
        await Task.WhenAll(shutdownTasks);

        _connections.Clear();
        _initialized = false;

        // 캐시 클리어
        InvalidateCache();

        _logger.LogInformation("MCP client shutdown completed");
    }

    /// <summary>
    /// 도구 캐시를 무효화합니다
    /// </summary>
    public void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _cachedTools = null;
            _logger.LogDebug("Tool cache invalidated");
        }
    }

    private async Task ConnectToServerAsync(McpServerConfig serverConfig, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Connecting to MCP server {ServerName} using command: {Command} {Args}",
                serverConfig.Name, serverConfig.Command, string.Join(" ", serverConfig.Args));

            var processStartInfo = new ProcessStartInfo
            {
                FileName = serverConfig.Command,
                Arguments = string.Join(" ", serverConfig.Args),
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Directory.GetCurrentDirectory(),
                StandardInputEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            foreach (var env in serverConfig.Env)
            {
                processStartInfo.Environment[env.Key] = env.Value;
            }

            _logger.LogDebug("Starting process with FileName='{FileName}' Arguments='{Arguments}'",
                processStartInfo.FileName, processStartInfo.Arguments);

            var process = new Process { StartInfo = processStartInfo };

            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start process for server {serverConfig.Name}");
            }

            ProcessJobManager.Instance.Assign(process);

            _logger.LogDebug("Process started with PID {ProcessId} for server {ServerName}",
                process.Id, serverConfig.Name);

            // Start error reading task
            _ = Task.Run(() => ReadProcessErrorsAsync(process, serverConfig.Name));

            var connection = new McpServerConnection(serverConfig.Name, process, _logger, _requestResponseLogger);

            _logger.LogDebug("Initializing MCP connection for server {ServerName}", serverConfig.Name);
            await connection.InitializeAsync(cancellationToken);
            _logger.LogDebug("MCP connection initialized for server {ServerName}", serverConfig.Name);

            _connections[serverConfig.Name] = connection;

            _logger.LogInformation("Successfully connected to MCP server {ServerName}", serverConfig.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to MCP server {ServerName}", serverConfig.Name);
            throw;
        }
    }

    private async Task ReadProcessErrorsAsync(Process process, string serverName)
    {
        try
        {
            if (process.StandardError == null) return;

            while (!process.HasExited)
            {
                var error = await process.StandardError.ReadLineAsync();
                if (!string.IsNullOrEmpty(error))
                {
                    _logger.LogWarning("MCP server {ServerName} stderr: {Error}", serverName, error);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading stderr from MCP server {ServerName}", serverName);
        }
    }

    private List<ToolDefinition> ConvertMcpToolsToDomainTools(List<McpTool> mcpTools)
    {
        var domainTools = new List<ToolDefinition>();

        foreach (var mcpTool in mcpTools)
        {
            try
            {
                var parameters = new Dictionary<string, ParameterDefinition>();

                if (mcpTool.InputSchema?.Properties != null)
                {
                    foreach (var prop in mcpTool.InputSchema.Properties)
                    {
                        var isRequired = mcpTool.InputSchema.Required?.Contains(prop.Key) ?? false;

                        parameters[prop.Key] = new ParameterDefinition
                        {
                            Type = prop.Value.Type,
                            Description = prop.Value.Description ?? "",
                            Required = isRequired
                        };
                    }
                }

                var domainTool = new ToolDefinition
                {
                    Name = mcpTool.Name,
                    Description = mcpTool.Description ?? "",
                    Parameters = parameters
                };

                domainTools.Add(domainTool);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to convert MCP tool {ToolName} to domain tool", mcpTool.Name);
            }
        }

        return domainTools;
    }

    private async void PerformHealthCheck(object? state)
    {
        var deadConnections = new List<string>();

        foreach (var kvp in _connections.ToList())
        {
            try
            {
                if (!await kvp.Value.IsHealthyAsync())
                {
                    _logger.LogWarning("MCP server {ServerName} is unhealthy, marking for reconnection", kvp.Key);
                    deadConnections.Add(kvp.Key);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check failed for MCP server {ServerName}", kvp.Key);
                deadConnections.Add(kvp.Key);
            }
        }

        // Attempt to reconnect dead connections
        foreach (var serverName in deadConnections)
        {
            try
            {
                if (_connections.TryGetValue(serverName, out var deadConnection))
                {
                    await deadConnection.DisposeAsync();
                    _connections.Remove(serverName);
                }

                var serverConfig = _config.Servers.FirstOrDefault(s => s.Name == serverName);
                if (serverConfig != null)
                {
                    _logger.LogInformation("Attempting to reconnect to MCP server {ServerName}", serverName);
                    await ConnectToServerAsync(serverConfig);
                    _logger.LogInformation("Successfully reconnected to MCP server {ServerName}", serverName);

                    // 재연결 시 도구 캐시 무효화
                    InvalidateCache();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reconnect to MCP server {ServerName}", serverName);
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _healthCheckTimer?.Dispose();

            foreach (var connection in _connections.Values)
            {
                try
                {
                    connection.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing connection to server {ServerName}", connection.ServerName);
                }
            }

            _connections.Clear();
            _disposed = true;
        }
    }
}