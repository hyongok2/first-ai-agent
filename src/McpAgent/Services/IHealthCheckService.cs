namespace McpAgent.Services;

public interface IHealthCheckService
{
    Task<HealthCheckResult> CheckLlmProviderAsync(CancellationToken cancellationToken = default);
    Task<HealthCheckResult> CheckMcpServersAsync(CancellationToken cancellationToken = default);
    Task<HealthCheckResult> CheckOverallHealthAsync(CancellationToken cancellationToken = default);
}

public class HealthCheckResult
{
    public bool IsHealthy { get; set; }
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, object> Details { get; set; } = new();
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}