using McpAgent.Configuration;
using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace McpAgent.Infrastructure.MCP;

/// <summary>
/// 여러 MCP 서버를 동시에 관리하는 Composite MCP 클라이언트 어댑터
/// </summary>
public class CompositeMcpClientAdapter : IMcpClientAdapter
{
    private readonly ILogger<CompositeMcpClientAdapter> _logger;
    private readonly IMcpClientFactory _clientFactory;
    private readonly Dictionary<string, IMcpClientAdapter> _clients = new();
    private readonly Dictionary<string, string> _toolToServerMap = new();
    private bool _isInitialized = false;
    private IReadOnlyList<ToolDefinition>? _cachedAllTools = null;
    private readonly object _toolsCacheLock = new();

    public CompositeMcpClientAdapter(
        ILogger<CompositeMcpClientAdapter> logger,
        IMcpClientFactory clientFactory,
        McpConfiguration mcpConfiguration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        
        if (mcpConfiguration?.Enabled == true && mcpConfiguration.Servers != null)
        {
            foreach (var serverConfig in mcpConfiguration.Servers)
            {
                try
                {
                    var client = _clientFactory.CreateClient(serverConfig);
                    _clients[serverConfig.Name] = client;
                    _logger.LogInformation("Created MCP client for server {ServerName} (Endpoint: {Endpoint})", 
                        serverConfig.Name, serverConfig.Endpoint);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create MCP client for server {ServerName}", serverConfig.Name);
                }
            }
        }

        if (_clients.Count == 0)
        {
            _logger.LogWarning("No MCP clients were successfully created");
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized) return;

        _logger.LogInformation("Initializing {ClientCount} MCP clients", _clients.Count);

        var initTasks = new List<Task>();
        foreach (var kvp in _clients)
        {
            initTasks.Add(InitializeClientAsync(kvp.Key, kvp.Value, cancellationToken));
        }

        await Task.WhenAll(initTasks);

        // 초기화 후 도구-서버 매핑 업데이트
        await UpdateToolServerMappingAsync(cancellationToken);

        _isInitialized = true;
        
        // 연결된 서버 수 확인
        var connectedServers = new List<string>();
        var failedServers = new List<string>();
        
        foreach (var kvp in _clients)
        {
            var servers = await kvp.Value.GetConnectedServersAsync();
            if (servers.Count > 0)
            {
                connectedServers.Add(kvp.Key);
            }
            else
            {
                failedServers.Add(kvp.Key);
            }
        }
        
        if (connectedServers.Count == 0)
        {
            _logger.LogError("No MCP servers could be connected. All {ServerCount} servers failed to initialize.", 
                failedServers.Count);
            _logger.LogError("Failed servers: {FailedServers}", string.Join(", ", failedServers));
        }
        else
        {
            _logger.LogInformation("Composite MCP client initialization complete. Connected: {ConnectedCount}/{TotalCount} servers", 
                connectedServers.Count, connectedServers.Count + failedServers.Count);
            
            if (connectedServers.Count > 0)
            {
                _logger.LogInformation("Connected servers: {ConnectedServers}", string.Join(", ", connectedServers));
            }
            
            if (failedServers.Count > 0)
            {
                _logger.LogWarning("Failed to connect to servers: {FailedServers}", string.Join(", ", failedServers));
            }
        }
    }

    private async Task InitializeClientAsync(string serverName, IMcpClientAdapter client, CancellationToken cancellationToken)
    {
        try
        {
            await client.InitializeAsync(cancellationToken);
            _logger.LogInformation("Successfully initialized MCP client for server {ServerName}", serverName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize MCP client for server {ServerName}", serverName);
            // 실패한 클라이언트는 제거
            _clients.Remove(serverName);
        }
    }

    private async Task UpdateToolServerMappingAsync(CancellationToken cancellationToken)
    {
        _toolToServerMap.Clear();

        foreach (var kvp in _clients)
        {
            try
            {
                var tools = await kvp.Value.GetAvailableToolsAsync(cancellationToken);
                foreach (var tool in tools)
                {
                    if (!_toolToServerMap.ContainsKey(tool.Name))
                    {
                        _toolToServerMap[tool.Name] = kvp.Key;
                        _logger.LogDebug("Mapped tool {ToolName} to server {ServerName}", tool.Name, kvp.Key);
                    }
                    else
                    {
                        _logger.LogWarning("Tool {ToolName} already exists on server {ExistingServer}, skipping duplicate from {NewServer}",
                            tool.Name, _toolToServerMap[tool.Name], kvp.Key);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get tools from server {ServerName}", kvp.Key);
            }
        }

        _logger.LogInformation("Tool-server mapping complete. Total tools: {ToolCount} across {ServerCount} servers",
            _toolToServerMap.Count, _clients.Count);
    }

    public async Task<IReadOnlyList<ToolDefinition>> GetAvailableToolsAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        // 캐시된 도구 목록이 있으면 바로 반환
        lock (_toolsCacheLock)
        {
            if (_cachedAllTools != null)
            {
                _logger.LogDebug("Returning cached composite tools ({ToolCount} tools)", _cachedAllTools.Count);
                return _cachedAllTools;
            }
        }

        var allTools = new List<ToolDefinition>();
        
        foreach (var kvp in _clients)
        {
            try
            {
                var tools = await kvp.Value.GetAvailableToolsAsync(cancellationToken);
                
                // 서버 이름을 도구의 카테고리나 메타데이터에 추가
                foreach (var tool in tools)
                {
                    // 도구 이름이 중복되지 않도록 체크
                    if (!allTools.Any(t => t.Name == tool.Name))
                    {
                        tool.Category = $"{tool.Category ?? "general"}@{kvp.Key}";
                        allTools.Add(tool);
                    }
                }
                
                _logger.LogDebug("Retrieved {ToolCount} tools from server {ServerName}", tools.Count, kvp.Key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get tools from server {ServerName}", kvp.Key);
            }
        }

        _logger.LogInformation("Total available tools: {ToolCount} from {ServerCount} servers", 
            allTools.Count, _clients.Count);

        // 도구 목록 캐시
        lock (_toolsCacheLock)
        {
            _cachedAllTools = allTools;
        }

        return allTools;
    }

    public async Task<object?> CallToolAsync(string toolName, Dictionary<string, object> arguments, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        // 도구를 제공하는 서버 찾기
        if (!_toolToServerMap.TryGetValue(toolName, out var serverName))
        {
            _logger.LogError("Tool {ToolName} not found in any MCP server", toolName);
            throw new InvalidOperationException($"Tool '{toolName}' not found in any MCP server");
        }

        if (!_clients.TryGetValue(serverName, out var client))
        {
            _logger.LogError("Server {ServerName} for tool {ToolName} is not available", serverName, toolName);
            throw new InvalidOperationException($"Server '{serverName}' for tool '{toolName}' is not available");
        }

        try
        {
            _logger.LogInformation("Executing tool {ToolName} on server {ServerName}", toolName, serverName);
            var result = await client.CallToolAsync(toolName, arguments, cancellationToken);
            _logger.LogInformation("Tool {ToolName} executed successfully on server {ServerName}", toolName, serverName);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute tool {ToolName} on server {ServerName}", toolName, serverName);
            
            // 서버가 실패한 경우, 다른 서버에서 동일한 도구를 찾아 시도 (fallback)
            var alternativeServer = await FindAlternativeServerForToolAsync(toolName, serverName, cancellationToken);
            if (alternativeServer != null)
            {
                _logger.LogInformation("Attempting fallback to server {AlternativeServer} for tool {ToolName}", 
                    alternativeServer, toolName);
                    
                return await _clients[alternativeServer].CallToolAsync(toolName, arguments, cancellationToken);
            }
            
            throw;
        }
    }

    private async Task<string?> FindAlternativeServerForToolAsync(string toolName, string excludeServer, CancellationToken cancellationToken)
    {
        foreach (var kvp in _clients)
        {
            if (kvp.Key == excludeServer) continue;

            try
            {
                var tools = await kvp.Value.GetAvailableToolsAsync(cancellationToken);
                if (tools.Any(t => t.Name == toolName))
                {
                    // 도구-서버 매핑 업데이트
                    _toolToServerMap[toolName] = kvp.Key;
                    return kvp.Key;
                }
            }
            catch
            {
                // 대체 서버 찾기 중 오류는 무시
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<string>> GetConnectedServersAsync()
    {
        var connectedServers = new List<string>();
        
        foreach (var kvp in _clients)
        {
            try
            {
                var servers = await kvp.Value.GetConnectedServersAsync();
                if (servers.Any())
                {
                    // 실제로 연결된 서버만 추가
                    connectedServers.AddRange(servers);
                }
            }
            catch (Exception ex)
            {
                // 개별 서버 오류는 로그에 기록하되 전체 프로세스는 계속
                _logger.LogDebug(ex, "Error checking connection status for server {ServerName}", kvp.Key);
            }
        }

        if (connectedServers.Count == 0)
        {
            _logger.LogDebug("No MCP servers are currently connected");
        }

        return connectedServers;
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Shutting down {ClientCount} MCP clients", _clients.Count);

        var shutdownTasks = new List<Task>();
        foreach (var kvp in _clients)
        {
            shutdownTasks.Add(ShutdownClientAsync(kvp.Key, kvp.Value, cancellationToken));
        }

        await Task.WhenAll(shutdownTasks);

        _clients.Clear();
        _toolToServerMap.Clear();
        _isInitialized = false;
        
        // 캐시 초기화
        lock (_toolsCacheLock)
        {
            _cachedAllTools = null;
        }

        _logger.LogInformation("All MCP clients have been shut down");
    }

    private async Task ShutdownClientAsync(string serverName, IMcpClientAdapter client, CancellationToken cancellationToken)
    {
        try
        {
            await client.ShutdownAsync(cancellationToken);
            _logger.LogInformation("Successfully shut down MCP client for server {ServerName}", serverName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to shut down MCP client for server {ServerName}", serverName);
        }
    }

    private void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException("Composite MCP client is not initialized. Call InitializeAsync first.");
        }
    }

    /// <summary>
    /// 특정 서버의 클라이언트를 직접 가져옵니다.
    /// </summary>
    public IMcpClientAdapter? GetClientForServer(string serverName)
    {
        return _clients.TryGetValue(serverName, out var client) ? client : null;
    }

    /// <summary>
    /// 활성화된 서버 목록을 가져옵니다.
    /// </summary>
    public IReadOnlyList<string> GetActiveServers()
    {
        return _clients.Keys.ToList();
    }
}