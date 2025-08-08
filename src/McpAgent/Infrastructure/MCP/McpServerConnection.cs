using System.Diagnostics;
using System.Text;
using System.Text.Json;
using McpAgent.Application.Interfaces;
using McpAgent.Infrastructure.MCP.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace McpAgent.Infrastructure.MCP;

public class McpServerConnection : IAsyncDisposable
{
    private readonly string _serverName;
    private readonly Process _process;
    private readonly ILogger _logger;
    private readonly IRequestResponseLogger? _requestResponseLogger;
    private readonly SemaphoreSlim _requestSemaphore = new(1, 1);
    private readonly ConcurrentDictionary<object, TaskCompletionSource<string>> _pendingRequests = new();
    private readonly Task _responseReaderTask;
    private readonly CancellationTokenSource _readerCancellation = new();
    private readonly object _nextIdLock = new();
    private int _nextRequestId = 1;
    private bool _disposed = false;
    private bool _initialized = false;

    public string ServerName => _serverName;
    public bool IsConnected => _process?.HasExited == false;

    public McpServerConnection(string serverName, Process process, ILogger logger, IRequestResponseLogger? requestResponseLogger = null)
    {
        _serverName = serverName;
        _process = process;
        _logger = logger;
        _requestResponseLogger = requestResponseLogger;
        
        // Start background task to read responses
        _responseReaderTask = Task.Run(ReadResponsesAsync, _readerCancellation.Token);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;

        try
        {
            _logger.LogInformation("Initializing MCP connection to server {ServerName}", _serverName);

            var initParams = new InitializeParams
            {
                ProtocolVersion = "2025-06-18",
                Capabilities = new InitializeCapabilities(),
                ClientInfo = new ClientInfo
                {
                    Name = "McpAgent",
                    Version = "1.0.0"
                }
            };

            var initRequest = new McpRequest
            {
                Id = GetNextRequestId(),
                Method = "initialize",
                Params = initParams
            };

            var response = await SendRequestAsync<InitializeResult>(initRequest, cancellationToken);
            
            if (response?.ServerInfo != null)
            {
                _logger.LogInformation("Successfully initialized connection to {ServerName} (version: {Version})", 
                    _serverName, response.ServerInfo.Version);
            }

            // Send initialized notification
            var initializedNotification = new McpRequest
            {
                Method = "notifications/initialized",
                Params = new { }
            };
            
            await SendNotificationAsync(initializedNotification, cancellationToken);
            
            _initialized = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize MCP connection to server {ServerName}", _serverName);
            throw;
        }
    }

    public async Task<List<McpTool>> GetToolsAsync(CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            throw new InvalidOperationException($"Server {_serverName} not initialized");
        }

        try
        {
            var request = new McpRequest
            {
                Id = GetNextRequestId(),
                Method = "tools/list",
                Params = new { }
            };

            var response = await SendRequestAsync<ListToolsResult>(request, cancellationToken);
            return response?.Tools ?? new List<McpTool>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get tools from server {ServerName}", _serverName);
            return new List<McpTool>();
        }
    }

    public async Task<object?> CallToolAsync(string toolName, Dictionary<string, object> arguments, CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            throw new InvalidOperationException($"Server {_serverName} not initialized");
        }

        var callParams = new CallToolParams
        {
            Name = toolName,
            Arguments = arguments
        };

        var request = new McpRequest
        {
            Id = GetNextRequestId(),
            Method = "tools/call",
            Params = callParams
        };

        var requestJson = JsonSerializer.Serialize(request, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            
            var response = await SendRequestAsync<CallToolResult>(request, cancellationToken);
            var responseJson = JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            stopwatch.Stop();

            // MCP 요청/응답 로깅 (요청 완료 직후 파싱 전에)
            if (_requestResponseLogger != null)
            {
                _ = Task.Run(() => _requestResponseLogger.LogMcpRequestResponseAsync(
                    _serverName,
                    toolName,
                    requestJson,
                    responseJson,
                    stopwatch.ElapsedMilliseconds,
                    CancellationToken.None));
            }
            
            if (response?.IsError == true)
            {
                var errorText = response.Content?.FirstOrDefault()?.Text ?? "Unknown error";
                _logger.LogError("Tool {ToolName} execution failed: {Error}", toolName, errorText);
                return new { error = errorText };
            }

            // Extract result from content
            var resultText = response?.Content?.FirstOrDefault()?.Text;
            if (string.IsNullOrEmpty(resultText))
            {
                return new { success = true };
            }

            // Try to parse as JSON, fallback to plain text
            try
            {
                return JsonSerializer.Deserialize<object>(resultText);
            }
            catch
            {
                return new { result = resultText };
            }
        }
        catch (Exception ex)
        {
            // 에러 발생시에도 로깅
            if (_requestResponseLogger != null)
            {
                _ = Task.Run(() => _requestResponseLogger.LogMcpRequestResponseAsync(
                    _serverName, 
                    toolName, 
                    requestJson, 
                    $"Error: {ex.Message}",
                    0, 
                    CancellationToken.None));
            }

            _logger.LogError(ex, "Failed to call tool {ToolName} on server {ServerName}", toolName, _serverName);
            return new { error = ex.Message };
        }
    }

    public async Task<bool> IsHealthyAsync()
    {
        try
        {
            if (_process?.HasExited != false)
            {
                return false;
            }

            return true; // 헬스체크를 이런 식으로 하면 안됨.
            // Test with actual MCP protocol - try to list tools
            var toolsRequest = new McpRequest
            {
                Id = GetNextRequestId(),
                Method = "tools/list",
                Params = new { }
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response = await SendRequestAsync<object>(toolsRequest, cts.Token);
            
            // Consider healthy if we get any response, even if no tools available
            return response != null;
        }
        catch
        {
            return false;
        }
    }

    private async Task<T?> SendRequestAsync<T>(McpRequest request, CancellationToken cancellationToken = default) where T : class
    {
        await _requestSemaphore.WaitAsync(cancellationToken);
        
        try
        {
            var requestId = request.Id;
            var tcs = new TaskCompletionSource<string>();
            
            _pendingRequests[requestId] = tcs;

            var requestJson = JsonSerializer.Serialize(request, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            _logger.LogDebug("Sending MCP request to {ServerName}: {Request}", _serverName, requestJson);

            await WriteToProcessAsync(requestJson + "\n", cancellationToken);

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCts.CancelAfter(TimeSpan.FromSeconds(30)); // 30 second timeout

            var responseJson = await tcs.Task.WaitAsync(linkedCts.Token);
            
            _logger.LogDebug("Received MCP response from {ServerName}: {Response}", _serverName, responseJson);

            var response = JsonSerializer.Deserialize<McpResponse<T>>(responseJson, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            if (response?.Error != null)
            {
                throw new InvalidOperationException($"MCP Error {response.Error.Code}: {response.Error.Message}");
            }

            return response?.Result;
        }
        finally
        {
            _pendingRequests.TryRemove(request.Id, out _);
            _requestSemaphore.Release();
        }
    }

    private async Task SendNotificationAsync(McpRequest notification, CancellationToken cancellationToken = default)
    {
        var notificationJson = JsonSerializer.Serialize(notification, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        _logger.LogDebug("Sending MCP notification to {ServerName}: {Notification}", _serverName, notificationJson);

        await WriteToProcessAsync(notificationJson + "\n", cancellationToken);
    }

    private async Task WriteToProcessAsync(string message, CancellationToken cancellationToken = default)
    {
        if (_process?.StandardInput == null || _process.HasExited)
        {
            throw new InvalidOperationException($"Process for server {_serverName} is not available");
        }

        var bytes = Encoding.UTF8.GetBytes(message);
        await _process.StandardInput.BaseStream.WriteAsync(bytes, cancellationToken);
        await _process.StandardInput.BaseStream.FlushAsync(cancellationToken);
    }

    private async Task ReadResponsesAsync()
    {
        try
        {
            if (_process?.StandardOutput == null)
            {
                _logger.LogError("StandardOutput not available for server {ServerName}", _serverName);
                return;
            }

            var reader = _process.StandardOutput;
            while (!_readerCancellation.Token.IsCancellationRequested && !_process.HasExited)
            {
                var line = await reader.ReadLineAsync();
                if (line == null) break;

                try
                {
                    ProcessResponse(line);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing response from server {ServerName}: {Response}", _serverName, line);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading responses from server {ServerName}", _serverName);
        }
    }

    private void ProcessResponse(string responseJson)
    {
        if (string.IsNullOrWhiteSpace(responseJson))
            return;

        _logger.LogDebug("Processing response from {ServerName}: {Response}", _serverName, responseJson);

        try
        {
            // Parse as generic response to get the ID
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            if (root.TryGetProperty("id", out var idElement))
            {
                object requestId;
                if (idElement.ValueKind == JsonValueKind.Number)
                {
                    requestId = idElement.GetInt32();
                }
                else
                {
                    requestId = idElement.GetString() ?? "";
                }

                if (_pendingRequests.TryRemove(requestId, out var tcs))
                {
                    tcs.SetResult(responseJson);
                }
                else
                {
                    _logger.LogWarning("Received response with unknown ID {Id} from server {ServerName}", requestId, _serverName);
                }
            }
            else if (root.TryGetProperty("method", out var methodElement))
            {
                // This is a notification from server - log it
                var method = methodElement.GetString();
                _logger.LogDebug("Received notification from server {ServerName}: {Method}", _serverName, method);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process response from server {ServerName}: {Response}", _serverName, responseJson);
        }
    }

    private object GetNextRequestId()
    {
        lock (_nextIdLock)
        {
            return _nextRequestId++;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        _disposed = true;
        _readerCancellation?.Cancel();

        try
        {
            if (_responseReaderTask != null)
            {
                // 짧은 타임아웃으로 reader task 대기
                await _responseReaderTask.WaitAsync(TimeSpan.FromMilliseconds(500));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error waiting for response reader task to complete for server {ServerName}", _serverName);
        }

        try
        {
            // 프로세스를 graceful하게 종료 시도
            if (_process != null && !_process.HasExited)
            {
                _logger.LogDebug("Attempting graceful shutdown of MCP server {ServerName} process", _serverName);
                
                // 먼저 stdin을 닫아서 graceful shutdown을 시도
                try
                {
                    _process.StandardInput?.Close();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to close stdin for server {ServerName}", _serverName);
                }
                
                // 짧게 대기해서 자연스럽게 종료되는지 확인
                if (!_process.WaitForExit(500))
                {
                    _logger.LogDebug("MCP server {ServerName} did not exit gracefully, will be terminated by ProcessJobManager", _serverName);
                }
            }
            
            _process?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing process for server {ServerName}", _serverName);
        }

        _requestSemaphore?.Dispose();
        _readerCancellation?.Dispose();

        //Complete any pending requests with cancellation
        foreach (var kvp in _pendingRequests)
        {
            kvp.Value.TrySetCanceled();
        }
        _pendingRequests.Clear();
        
        _logger.LogDebug("MCP server connection {ServerName} disposed successfully", _serverName);
    }
}