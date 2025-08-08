using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace McpAgent.Infrastructure.Storage;

/// <summary>
/// LLM 요약 기반의 메모리 효율적인 대화 저장소 구현
/// </summary>
public class InMemorySummarizedConversationRepository : ISummarizedConversationRepository
{
    private readonly ConcurrentDictionary<string, SummarizedConversation> _conversations = new();
    private readonly ILogger<InMemorySummarizedConversationRepository> _logger;

    public InMemorySummarizedConversationRepository(ILogger<InMemorySummarizedConversationRepository> logger)
    {
        _logger = logger;
    }

    public Task<SummarizedConversation?> GetByIdAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        _conversations.TryGetValue(conversationId, out var conversation);
        return Task.FromResult(conversation);
    }

    public Task<SummarizedConversation> CreateAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        var conversation = new SummarizedConversation(conversationId);
        _conversations.TryAdd(conversationId, conversation);
        
        _logger.LogInformation("Created new summarized conversation {ConversationId}", conversationId);
        return Task.FromResult(conversation);
    }

    public Task AddMessageToCurrentTurnAsync(string conversationId, ConversationMessage message, CancellationToken cancellationToken = default)
    {
        if (_conversations.TryGetValue(conversationId, out var conversation))
        {
            conversation.AddCurrentTurnMessage(message);
            _logger.LogDebug("Added message to current turn for conversation {ConversationId}: {Role}", conversationId, message.Role);
        }
        else
        {
            _logger.LogWarning("Conversation {ConversationId} not found when adding message", conversationId);
        }
        
        return Task.CompletedTask;
    }

    public Task CompleteTurnWithSummaryAsync(string conversationId, TurnSummary turnSummary, CancellationToken cancellationToken = default)
    {
        if (_conversations.TryGetValue(conversationId, out var conversation))
        {
            conversation.CompleteTurnWithSummary(turnSummary);
            
            _logger.LogInformation("Completed turn {TurnNumber} for conversation {ConversationId} with summary", 
                turnSummary.TurnNumber, conversationId);
            
            // 메모리 사용량 로깅
            LogMemoryStats(conversationId, conversation);
        }
        else
        {
            _logger.LogWarning("Conversation {ConversationId} not found when completing turn", conversationId);
        }
        
        return Task.CompletedTask;
    }

    public Task UpdateConsolidatedSummaryAsync(string conversationId, string consolidatedSummary, CancellationToken cancellationToken = default)
    {
        if (_conversations.TryGetValue(conversationId, out var conversation))
        {
            conversation.UpdateConsolidatedSummary(consolidatedSummary);
            _logger.LogInformation("Updated consolidated summary for conversation {ConversationId}", conversationId);
        }
        else
        {
            _logger.LogWarning("Conversation {ConversationId} not found when updating consolidated summary", conversationId);
        }
        
        return Task.CompletedTask;
    }

    public Task<string> GetFullConversationContextAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        if (_conversations.TryGetValue(conversationId, out var conversation))
        {
            var context = conversation.GetFullConversationContext();
            _logger.LogDebug("Retrieved full conversation context for {ConversationId} ({Length} chars)", 
                conversationId, context.Length);
            return Task.FromResult(context);
        }
        
        _logger.LogWarning("Conversation {ConversationId} not found when getting context", conversationId);
        return Task.FromResult("새로운 대화입니다.");
    }

    public Task<IReadOnlyList<string>> GetActiveConversationIdsAsync(CancellationToken cancellationToken = default)
    {
        var activeIds = _conversations.Values
            .Where(c => c.Status == ConversationStatus.Active)
            .Select(c => c.Id)
            .ToList();
        
        return Task.FromResult<IReadOnlyList<string>>(activeIds.AsReadOnly());
    }

    public Task DeleteAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        var removed = _conversations.TryRemove(conversationId, out _);
        
        if (removed)
        {
            _logger.LogInformation("Deleted conversation {ConversationId}", conversationId);
        }
        else
        {
            _logger.LogWarning("Conversation {ConversationId} not found for deletion", conversationId);
        }
        
        return Task.CompletedTask;
    }

    public Task SetStatusAsync(string conversationId, ConversationStatus status, CancellationToken cancellationToken = default)
    {
        if (_conversations.TryGetValue(conversationId, out var conversation))
        {
            conversation.SetStatus(status);
            _logger.LogInformation("Set conversation {ConversationId} status to {Status}", conversationId, status);
        }
        else
        {
            _logger.LogWarning("Conversation {ConversationId} not found when setting status", conversationId);
        }
        
        return Task.CompletedTask;
    }
    
    private void LogMemoryStats(string conversationId, SummarizedConversation conversation)
    {
        var currentMessages = conversation.CurrentTurnMessages.Count;
        var totalTurns = conversation.Summary.TotalTurns;
        var individualSummaries = conversation.Summary.IndividualTurns.Count;
        var hasConsolidated = !string.IsNullOrEmpty(conversation.Summary.ConsolidatedSummary);
        
        _logger.LogDebug("Memory stats for {ConversationId}: Current messages={CurrentMessages}, " +
                        "Total turns={TotalTurns}, Individual summaries={IndividualSummaries}, " +
                        "Has consolidated={HasConsolidated}", 
                        conversationId, currentMessages, totalTurns, individualSummaries, hasConsolidated);
    }
}