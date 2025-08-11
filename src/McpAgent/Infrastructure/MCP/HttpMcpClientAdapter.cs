using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using McpAgent.Application.Interfaces;
using McpAgent.Configuration;
using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;
using McpAgent.Infrastructure.MCP.Models;
using Microsoft.Extensions.Logging;

namespace McpAgent.Infrastructure.MCP;

/// <summary>
/// HTTP 기반 MCP 클라이언트 어댑터
/// </summary>
public class HttpMcpClientAdapter : IMcpClientAdapter
{
    private readonly ILogger<HttpMcpClientAdapter> _logger;
    private readonly McpServerConfig _serverConfig;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IRequestResponseLogger? _requestResponseLogger;
    private readonly object _nextIdLock = new();
    private int _nextRequestId = 1;
    private bool _isInitialized = false;

    public HttpMcpClientAdapter(
        ILogger<HttpMcpClientAdapter> logger,
        McpServerConfig serverConfig,
        IHttpClientFactory httpClientFactory,
        IRequestResponseLogger? requestResponseLogger = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serverConfig = serverConfig ?? throw new ArgumentNullException(nameof(serverConfig));
        _requestResponseLogger = requestResponseLogger;
        
        if (string.IsNullOrEmpty(serverConfig.Endpoint))
        {
            throw new ArgumentException("HTTP endpoint is required", nameof(serverConfig));
        }

        _httpClient = httpClientFactory.CreateClient($"mcp-{serverConfig.Name}");
        _httpClient.BaseAddress = new Uri(serverConfig.Endpoint);
        _httpClient.Timeout = TimeSpan.FromSeconds(serverConfig.TimeoutSeconds);

        // 기본 헤더 설정
        if (!string.IsNullOrEmpty(serverConfig.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = 
                new AuthenticationHeaderValue("Bearer", serverConfig.ApiKey);
        }

        foreach (var header in serverConfig.Headers)
        {
            _httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
        }

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing HTTP MCP client for server {ServerName} at {Endpoint}", 
            _serverConfig.Name, _serverConfig.Endpoint);

        try
        {
            // MCP 초기화 프로토콜 시도
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5)); // 5초 타임아웃
            
            // MCP 표준 초기화 요청 (JSON-RPC 2.0)
            var initRequest = new
            {
                jsonrpc = "2.0",
                id = GetNextRequestId(),
                method = "initialize",
                @params = new
                {
                    protocolVersion = "2025-06-18",
                    capabilities = new
                    {
                        tools = new { }
                    },
                    clientInfo = new
                    {
                        name = "McpAgent",
                        version = "1.0.0"
                    }
                }
            };
            
            var response = await SendRequestAsync<object>(
                HttpMethod.Post,
                "/mcp",
                initRequest,
                timeoutCts.Token);
            
            _isInitialized = true;
            _logger.LogInformation("Successfully initialized HTTP MCP client for server {ServerName}", 
                _serverConfig.Name);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // 사용자가 취소한 경우에는 그대로 throw
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to HTTP MCP server {ServerName} at {Endpoint}. Server may be offline.", 
                _serverConfig.Name, _serverConfig.Endpoint);
            
            // 연결 실패해도 일단 초기화는 완료로 처리 (나중에 도구 호출할 때 다시 시도)
            _isInitialized = true;
            _logger.LogInformation("HTTP MCP client for server {ServerName} initialized in offline mode", 
                _serverConfig.Name);
        }
    }

    public async Task<IReadOnlyList<ToolDefinition>> GetAvailableToolsAsync(CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        try
        {
            // MCP 표준 도구 목록 요청 (JSON-RPC 2.0)
            var toolsRequest = new
            {
                jsonrpc = "2.0",
                id = GetNextRequestId(),
                method = "tools/list",
                @params = new { }
            };

            var response = await SendRequestAsync<McpResponse<ListToolsResult>>(
                HttpMethod.Post,
                "/mcp",
                toolsRequest,
                cancellationToken);

            _logger.LogDebug("Tools/list response from server {ServerName}: {@Response}", _serverConfig.Name, response);

            if (response?.Result?.Tools == null)
            {
                _logger.LogWarning("No tools found in response from server {ServerName}. Response: {@Response}", _serverConfig.Name, response);
                return Array.Empty<ToolDefinition>();
            }

            var tools = response.Result.Tools.Select(t => new ToolDefinition
            {
                Name = t.Name,
                Description = t.Description ?? "",
                Parameters = ConvertSchemaToParameters(t.InputSchema),
                Schema = JsonSerializer.Serialize(t.InputSchema, _jsonOptions)
            }).ToList();

            _logger.LogInformation("Successfully loaded {ToolCount} tools from server {ServerName}: {ToolNames}", 
                tools.Count, _serverConfig.Name, string.Join(", ", tools.Select(t => t.Name)));

            // 각 도구의 파라미터 정보 로그 출력
            foreach (var tool in tools.Take(2)) // 처음 2개만 상세 로그
            {
                _logger.LogInformation("Tool {ToolName} schema: {Schema}", tool.Name, tool.Schema);
                _logger.LogInformation("Tool {ToolName} parameters count: {Count}", tool.Name, tool.Parameters.Count);
                
                foreach (var (paramName, paramDef) in tool.Parameters)
                {
                    _logger.LogInformation("  Parameter {ParamName}: Type={Type}, Required={Required}, Description={Description}", 
                        paramName, paramDef.Type, paramDef.Required, paramDef.Description);
                }
            }

            return tools;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get available tools from HTTP MCP server {ServerName}. Server may be offline.", 
                _serverConfig.Name);
            
            // 서버가 오프라인이면 빈 도구 리스트 반환
            return Array.Empty<ToolDefinition>();
        }
    }

    public async Task<object?> CallToolAsync(string toolName, Dictionary<string, object> arguments, CancellationToken cancellationToken = default)
    {
        EnsureInitialized();

        // MCP 표준 도구 실행 요청 (JSON-RPC 2.0)
        var request = new
        {
            jsonrpc = "2.0",
            id = GetNextRequestId(),
            method = "tools/call",
            @params = new
            {
                name = toolName,
                arguments = arguments
            }
        };

        var requestJson = JsonSerializer.Serialize(request, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true  // 로그 가독성을 위해 들여쓰기 추가
        });

        try
        {
            _logger.LogDebug("Calling tool {ToolName} on HTTP MCP server {ServerName} with arguments: {Arguments}",
                toolName, _serverConfig.Name, JsonSerializer.Serialize(arguments, _jsonOptions));

            var stopwatch = Stopwatch.StartNew();

            var response = await SendRequestAsync<McpResponse<CallToolResult>>(
                HttpMethod.Post,
                "/mcp",
                request,
                cancellationToken);

            var responseJson = JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true  // 로그 가독성을 위해 들여쓰기 추가
            });
            
            stopwatch.Stop();

            // MCP 요청/응답 로깅 (요청 완료 직후)
            if (_requestResponseLogger != null)
            {
                _ = Task.Run(() => _requestResponseLogger.LogMcpRequestResponseAsync(
                    _serverConfig.Name,
                    toolName,
                    requestJson,
                    responseJson,
                    stopwatch.ElapsedMilliseconds,
                    CancellationToken.None));
            }

            if (response?.Result?.IsError == true)
            {
                var errorText = response.Result.Content?.FirstOrDefault()?.Text ?? "Unknown error";
                _logger.LogError("Tool {ToolName} execution failed: {Error}", toolName, errorText);
                return new { error = errorText };
            }

            // Extract result from content
            var resultText = response?.Result?.Content?.FirstOrDefault()?.Text;
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
                    _serverConfig.Name,
                    toolName,
                    requestJson,
                    $"Error: {ex.Message}",
                    0,
                    CancellationToken.None));
            }

            _logger.LogError(ex, "Failed to call tool {ToolName} on HTTP MCP server {ServerName}", 
                toolName, _serverConfig.Name);
            throw;
        }
    }

    public Task<IReadOnlyList<string>> GetConnectedServersAsync()
    {
        if (_isInitialized)
        {
            return Task.FromResult<IReadOnlyList<string>>(new[] { _serverConfig.Name });
        }
        return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    public Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Shutting down HTTP MCP client for server {ServerName}", _serverConfig.Name);
        _isInitialized = false;
        _httpClient?.Dispose();
        return Task.CompletedTask;
    }

    private void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException($"HTTP MCP client for server {_serverConfig.Name} is not initialized");
        }
    }

    private int GetNextRequestId()
    {
        lock (_nextIdLock)
        {
            return _nextRequestId++;
        }
    }

    private Dictionary<string, ParameterDefinition> ConvertSchemaToParameters(McpToolInputSchema inputSchema)
    {
        var parameters = new Dictionary<string, ParameterDefinition>();
        
        if (inputSchema?.Properties != null)
        {
            foreach (var (paramName, property) in inputSchema.Properties)
            {
                var paramDef = new ParameterDefinition
                {
                    Type = property.Type,
                    Description = property.Description ?? "",
                    Required = inputSchema.Required?.Contains(paramName) ?? false
                };
                
                parameters[paramName] = paramDef;
            }
        }
        
        return parameters;
    }

    private async Task<TResponse?> SendRequestAsync<TResponse>(
        HttpMethod method,
        string path,
        object? requestBody,
        CancellationToken cancellationToken) where TResponse : class
    {
        using var request = new HttpRequestMessage(method, path);
        
        if (requestBody != null)
        {
            var json = JsonSerializer.Serialize(requestBody, _jsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        
        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        
        _logger.LogDebug("HTTP MCP response from {ServerName}: Status={StatusCode}, Content={Content}",
            _serverConfig.Name, response.StatusCode, responseContent);
        
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("HTTP MCP request failed with status {StatusCode}: {Content}",
                response.StatusCode, responseContent);
            throw new HttpRequestException($"HTTP MCP request failed with status {response.StatusCode}: {responseContent}");
        }

        if (string.IsNullOrEmpty(responseContent))
        {
            return null;
        }

        try
        {
            var deserializedResponse = JsonSerializer.Deserialize<TResponse>(responseContent, _jsonOptions);
            _logger.LogDebug("Successfully deserialized response to {ResponseType} from server {ServerName}", 
                typeof(TResponse).Name, _serverConfig.Name);
            return deserializedResponse;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize JSON response from server {ServerName}. Content: {Content}", 
                _serverConfig.Name, responseContent);
            throw;
        }
    }

}