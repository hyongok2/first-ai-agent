namespace McpAgent.Services;

public interface ISessionManager
{
    Task<string> GetOrCreateSessionAsync(string? sessionId = null, CancellationToken cancellationToken = default);
    Task<bool> SessionExistsAsync(string sessionId, CancellationToken cancellationToken = default);
    Task ClearSessionAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<List<string>> GetActiveSessionsAsync(CancellationToken cancellationToken = default);
}