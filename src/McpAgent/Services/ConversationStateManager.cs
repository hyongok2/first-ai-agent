using System.Collections.Concurrent;
using McpAgent.Models;
using Microsoft.Extensions.Logging;

namespace McpAgent.Services;

public interface IConversationStateManager
{
    Task<ConversationState> GetStateAsync(string conversationId, CancellationToken cancellationToken = default);
    Task SaveStateAsync(ConversationState state, CancellationToken cancellationToken = default);
    Task<ConversationState> CreateNewStateAsync(string? conversationId = null, CancellationToken cancellationToken = default);
    Task<bool> IsStuckInLoopAsync(ConversationState state);
    Task ClearStateAsync(string conversationId, CancellationToken cancellationToken = default);
}

public class ConversationStateManager : IConversationStateManager
{
    private readonly ILogger<ConversationStateManager> _logger;
    private readonly ConcurrentDictionary<string, ConversationState> _states = new();
    private readonly Timer _cleanupTimer;
    
    public ConversationStateManager(ILogger<ConversationStateManager> logger)
    {
        _logger = logger;
        
        // 1시간마다 오래된 상태 정리
        _cleanupTimer = new Timer(CleanupOldStates, null, 
            TimeSpan.FromHours(1), TimeSpan.FromHours(1));
    }
    
    public async Task<ConversationState> GetStateAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        
        if (_states.TryGetValue(conversationId, out var existingState))
        {
            existingState.LastActivity = DateTime.UtcNow;
            return existingState;
        }
        
        return await CreateNewStateAsync(conversationId, cancellationToken);
    }
    
    public async Task SaveStateAsync(ConversationState state, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        
        state.LastActivity = DateTime.UtcNow;
        _states.AddOrUpdate(state.ConversationId, state, (key, oldState) => state);
        
        _logger.LogDebug("Saved conversation state for {ConversationId}, Phase: {Phase}", 
            state.ConversationId, state.CurrentPhase);
    }
    
    public async Task<ConversationState> CreateNewStateAsync(string? conversationId = null, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        
        var state = new ConversationState
        {
            ConversationId = conversationId ?? Guid.NewGuid().ToString(),
            CurrentPhase = 1,
            LastActivity = DateTime.UtcNow
        };
        
        _states[state.ConversationId] = state;
        
        _logger.LogDebug("Created new conversation state for {ConversationId}", state.ConversationId);
        return state;
    }
    
    public async Task<bool> IsStuckInLoopAsync(ConversationState state)
    {
        await Task.CompletedTask;
        
        // 루프 감지 로직
        var loopContext = state.LoopContext;
        
        // 전체 루프 히스토리에서 패턴 감지
        if (loopContext.LoopHistory.Count >= 10)
        {
            var recentHistory = loopContext.LoopHistory.TakeLast(10).ToList();
            
            // 같은 단계를 3번 이상 반복하는지 확인
            var phaseFrequency = recentHistory
                .GroupBy(l => l.FromPhase)
                .Where(g => g.Count() >= 3);
                
            if (phaseFrequency.Any())
            {
                _logger.LogWarning("Loop detected in conversation {ConversationId}: Phase {Phase} repeated {Count} times", 
                    state.ConversationId, 
                    phaseFrequency.First().Key, 
                    phaseFrequency.First().Count());
                return true;
            }
            
            // 연속된 같은 패턴 감지 (1->2->1->2 등)
            for (int i = 0; i < recentHistory.Count - 3; i++)
            {
                if (recentHistory[i].FromPhase == recentHistory[i + 2].FromPhase &&
                    recentHistory[i].ToPhase == recentHistory[i + 2].ToPhase &&
                    recentHistory[i + 1].FromPhase == recentHistory[i + 3].FromPhase &&
                    recentHistory[i + 1].ToPhase == recentHistory[i + 3].ToPhase)
                {
                    _logger.LogWarning("Oscillation pattern detected in conversation {ConversationId}", 
                        state.ConversationId);
                    return true;
                }
            }
        }
        
        // 개별 단계의 반복 횟수 체크
        return loopContext.PhaseLoopCounts.Values.Any(count => count >= loopContext.MaxLoopIterations);
    }
    
    public async Task ClearStateAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        
        if (_states.TryRemove(conversationId, out var removedState))
        {
            _logger.LogDebug("Cleared conversation state for {ConversationId}", conversationId);
        }
    }
    
    private void CleanupOldStates(object? state)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddHours(-2); // 2시간 이상 비활성 상태 제거
            var toRemove = _states
                .Where(kvp => kvp.Value.LastActivity < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();
                
            foreach (var conversationId in toRemove)
            {
                _states.TryRemove(conversationId, out _);
            }
            
            if (toRemove.Any())
            {
                _logger.LogDebug("Cleaned up {Count} inactive conversation states", toRemove.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during conversation state cleanup");
        }
    }
    
    public void Dispose()
    {
        _cleanupTimer?.Dispose();
    }
}