using System.Diagnostics;
using System.Text;
using System.Text.Json;
using McpAgent.Common;
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
    private readonly Timer? _healthCheckTimer;

    public McpClient(ILogger<McpClient> logger, IOptions<AgentConfiguration> options)
    {
        _logger = logger;
        _config = options.Value.Mcp;
        
        // Start health check timer (every 30 seconds)
        _healthCheckTimer = new Timer(PerformHealthCheck, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
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
        _healthCheckTimer?.Dispose();
        var disconnectTasks = _connections.Keys.Select(serverName => DisconnectAsync(serverName, cancellationToken));
        await Task.WhenAll(disconnectTasks);
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
                    _logger.LogWarning("MCP server {ServerName} is unhealthy, attempting reconnection", kvp.Key);
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
                await DisconnectAsync(serverName);
                await ConnectAsync(serverName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reconnect to MCP server {ServerName}", serverName);
            }
        }
    }
}

internal class McpServerConnection : IAsyncDisposable
{
    private readonly string _serverName;
    private readonly Process _process;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _requestSemaphore = new(1, 1);
    private readonly object _responseLock = new();
    private readonly Queue<string> _responseBuffer = new();
    private readonly Task _responseReaderTask;
    private readonly CancellationTokenSource _readerCancellation = new();

    public string ServerName => _serverName;

    public McpServerConnection(string serverName, Process process, ILogger logger)
    {
        _serverName = serverName;
        _process = process;
        _logger = logger;
        
        // Start background task to read responses
        _responseReaderTask = Task.Run(ReadResponsesAsync, _readerCancellation.Token);
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
                protocolVersion = "2025-06-18",
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
            
            // Send request
            await _process.StandardInput.WriteLineAsync(json);
            await _process.StandardInput.FlushAsync();

            // Wait for response with timeout
            var response = await WaitForResponseAsync(TimeSpan.FromSeconds(30), cancellationToken);
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

    private async Task<string?> WaitForResponseAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            lock (_responseLock)
            {
                if (_responseBuffer.Count > 0)
                {
                    return _responseBuffer.Dequeue();
                }
            }
            
            await Task.Delay(10, cancellationToken); // Small delay to prevent tight loop
        }
        
        return null;
    }

    private async Task ReadResponsesAsync()
    {
        try
        {
            var buffer = new StringBuilder();
            var reader = _process.StandardOutput;
            
            while (!_readerCancellation.IsCancellationRequested && !_process.HasExited)
            {
                var line = await reader.ReadLineAsync();
                if (line == null) break;
                
                buffer.AppendLine(line);
                
                // Try to parse as complete JSON response
                var content = buffer.ToString().Trim();
                if (IsCompleteJsonResponse(content))
                {
                    lock (_responseLock)
                    {
                        _responseBuffer.Enqueue(content);
                    }
                    buffer.Clear();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading responses from MCP server {ServerName}", _serverName);
        }
    }

    private bool IsCompleteJsonResponse(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            return doc.RootElement.TryGetProperty("jsonrpc", out _);
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> IsHealthyAsync()
    {
        try
        {
            return !_process.HasExited && _process.Responding;
        }
        catch
        {
            return false;
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
            _logger.LogDebug("Starting graceful shutdown of MCP server {ServerName}", _serverName);
            
            // Cancel the response reader
            _readerCancellation.Cancel();
            
            try
            {
                await _responseReaderTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelling
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Response reader task for {ServerName} did not complete within timeout", _serverName);
            }
            
            // Attempt graceful shutdown first
            if (!_process.HasExited)
            {
                try
                {
                    // Close stdin to signal the server to shutdown gracefully
                    _process.StandardInput.Close();
                    
                    // Wait for graceful exit with timeout
                    var gracefulExitTask = _process.WaitForExitAsync();
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    
                    try
                    {
                        await gracefulExitTask.WaitAsync(timeoutCts.Token);
                        _logger.LogDebug("MCP server {ServerName} exited gracefully", _serverName);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogWarning("MCP server {ServerName} did not exit gracefully within timeout, forcing termination", _serverName);
                        await ForceTerminateProcess();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to gracefully shutdown MCP server {ServerName}, forcing termination", _serverName);
                    await ForceTerminateProcess();
                }
            }
            else
            {
                _logger.LogDebug("MCP server {ServerName} already exited", _serverName);
            }
            
            _process.Dispose();
            _logger.LogInformation("MCP server {ServerName} shutdown completed", _serverName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing MCP server connection {ServerName}", _serverName);
        }
        finally
        {
            _requestSemaphore.Dispose();
            _readerCancellation.Dispose();
        }
    }

    private async Task ForceTerminateProcess()
    {
        try
        {
            if (!_process.HasExited)
            {
                // Kill the entire process tree to ensure all child processes are terminated
                await KillProcessTree(_process.Id);
                
                // Wait for the process to actually exit
                if (!_process.WaitForExit(2000)) // 2 second timeout
                {
                    _logger.LogError("Failed to terminate MCP server {ServerName} within timeout", _serverName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error force terminating MCP server {ServerName}", _serverName);
        }
    }

    private async Task KillProcessTree(int processId)
    {
        try
        {
            // Use taskkill on Windows to kill the process tree
            if (OperatingSystem.IsWindows())
            {
                var killProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "taskkill",
                        Arguments = $"/F /T /PID {processId}",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                
                killProcess.Start();
                await killProcess.WaitForExitAsync();
                
                if (killProcess.ExitCode == 0)
                {
                    _logger.LogDebug("Successfully killed process tree for PID {ProcessId}", processId);
                }
                else
                {
                    _logger.LogWarning("taskkill returned exit code {ExitCode} for PID {ProcessId}", killProcess.ExitCode, processId);
                }
            }
            else
            {
                // Use kill on Unix-like systems
                var killProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "kill",
                        Arguments = $"-TERM -{processId}", // Kill process group
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                
                killProcess.Start();
                await killProcess.WaitForExitAsync();
                
                // If TERM doesn't work, use KILL
                if (!_process.HasExited)
                {
                    await Task.Delay(1000); // Give it a moment
                    if (!_process.HasExited)
                    {
                        var forceKillProcess = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "kill",
                                Arguments = $"-KILL -{processId}",
                                UseShellExecute = false,
                                CreateNoWindow = true
                            }
                        };
                        
                        forceKillProcess.Start();
                        await forceKillProcess.WaitForExitAsync();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error killing process tree for PID {ProcessId}", processId);
            // Fallback to simple Kill() if process tree kill fails
            if (!_process.HasExited)
            {
                _process.Kill();
            }
        }
    }
}