using System.Collections.Concurrent;
using McpAgent.Memory;
using Microsoft.Extensions.Logging;

namespace McpAgent.Services;

public class SessionManager : ISessionManager
{
    private readonly ILogger<SessionManager> _logger;
    private readonly IConversationManager _conversationManager;
    private readonly ConcurrentDictionary<string, DateTime> _activeSessions = new();
    private readonly Timer _cleanupTimer;

    public SessionManager(ILogger<SessionManager> logger, IConversationManager conversationManager)
    {
        _logger = logger;
        _conversationManager = conversationManager;
        
        // Clean up inactive sessions every 30 minutes
        _cleanupTimer = new Timer(CleanupInactiveSessions, null, TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30));
    }

    public async Task<string> GetOrCreateSessionAsync(string? sessionId = null, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(sessionId) && await SessionExistsAsync(sessionId, cancellationToken))
        {
            _activeSessions[sessionId] = DateTime.UtcNow;
            _logger.LogDebug("Resumed existing session {SessionId}", sessionId);
            return sessionId;
        }

        var newSessionId = await _conversationManager.CreateConversationAsync(cancellationToken);
        _activeSessions[newSessionId] = DateTime.UtcNow;
        
        _logger.LogInformation("Created new session {SessionId}", newSessionId);
        return newSessionId;
    }

    public async Task<bool> SessionExistsAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        return _activeSessions.ContainsKey(sessionId) && 
               await _conversationManager.ConversationExistsAsync(sessionId, cancellationToken);
    }

    public async Task ClearSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _activeSessions.TryRemove(sessionId, out _);
        await _conversationManager.ClearHistoryAsync(sessionId, cancellationToken);
        _logger.LogInformation("Cleared session {SessionId}", sessionId);
    }

    public Task<List<string>> GetActiveSessionsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_activeSessions.Keys.ToList());
    }

    private void CleanupInactiveSessions(object? state)
    {
        var cutoffTime = DateTime.UtcNow.AddHours(-24); // Sessions older than 24 hours
        var inactiveSessions = _activeSessions
            .Where(kvp => kvp.Value < cutoffTime)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var sessionId in inactiveSessions)
        {
            _activeSessions.TryRemove(sessionId, out _);
            _logger.LogDebug("Cleaned up inactive session {SessionId}", sessionId);
        }

        if (inactiveSessions.Count > 0)
        {
            _logger.LogInformation("Cleaned up {Count} inactive sessions", inactiveSessions.Count);
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}