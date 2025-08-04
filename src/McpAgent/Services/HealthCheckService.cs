using McpAgent.Mcp;
using McpAgent.Providers;
using Microsoft.Extensions.Logging;

namespace McpAgent.Services;

public class HealthCheckService : IHealthCheckService
{
    private readonly ILogger<HealthCheckService> _logger;
    private readonly ILlmProvider _llmProvider;
    private readonly IMcpClient _mcpClient;

    public HealthCheckService(
        ILogger<HealthCheckService> logger,
        ILlmProvider llmProvider,
        IMcpClient mcpClient)
    {
        _logger = logger;
        _llmProvider = llmProvider;
        _mcpClient = mcpClient;
    }

    public async Task<HealthCheckResult> CheckLlmProviderAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var isAvailable = await _llmProvider.IsAvailableAsync(cancellationToken);
            
            return new HealthCheckResult
            {
                IsHealthy = isAvailable,
                Message = isAvailable ? "LLM provider is available" : "LLM provider is not available",
                Details = new Dictionary<string, object>
                {
                    ["provider"] = "ollama",
                    ["available"] = isAvailable
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check LLM provider health");
            return new HealthCheckResult
            {
                IsHealthy = false,
                Message = $"LLM provider health check failed: {ex.Message}",
                Details = new Dictionary<string, object>
                {
                    ["error"] = ex.Message,
                    ["provider"] = "ollama"
                }
            };
        }
    }

    public async Task<HealthCheckResult> CheckMcpServersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var connectedServers = await _mcpClient.GetConnectedServersAsync();
            var availableTools = await _mcpClient.GetAvailableToolsAsync(cancellationToken);
            
            var isHealthy = connectedServers.Count > 0;
            
            return new HealthCheckResult
            {
                IsHealthy = isHealthy,
                Message = isHealthy 
                    ? $"MCP servers are healthy ({connectedServers.Count} connected)"
                    : "No MCP servers are connected",
                Details = new Dictionary<string, object>
                {
                    ["connectedServers"] = connectedServers,
                    ["serverCount"] = connectedServers.Count,
                    ["availableTools"] = availableTools.Count,
                    ["toolNames"] = availableTools.Select(t => t.Name).ToList()
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check MCP servers health");
            return new HealthCheckResult
            {
                IsHealthy = false,
                Message = $"MCP servers health check failed: {ex.Message}",
                Details = new Dictionary<string, object>
                {
                    ["error"] = ex.Message
                }
            };
        }
    }

    public async Task<HealthCheckResult> CheckOverallHealthAsync(CancellationToken cancellationToken = default)
    {
        var llmHealth = await CheckLlmProviderAsync(cancellationToken);
        var mcpHealth = await CheckMcpServersAsync(cancellationToken);
        
        var isHealthy = llmHealth.IsHealthy && mcpHealth.IsHealthy;
        var messages = new List<string>();
        
        if (!llmHealth.IsHealthy)
            messages.Add(llmHealth.Message);
        if (!mcpHealth.IsHealthy)
            messages.Add(mcpHealth.Message);
            
        return new HealthCheckResult
        {
            IsHealthy = isHealthy,
            Message = isHealthy 
                ? "All systems are healthy" 
                : string.Join("; ", messages),
            Details = new Dictionary<string, object>
            {
                ["llm"] = llmHealth,
                ["mcp"] = mcpHealth,
                ["overallStatus"] = isHealthy ? "healthy" : "unhealthy"
            }
        };
    }
}