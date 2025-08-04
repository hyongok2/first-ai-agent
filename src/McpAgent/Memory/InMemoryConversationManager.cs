using System.Collections.Concurrent;
using McpAgent.Configuration;
using McpAgent.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace McpAgent.Memory;

public class InMemoryConversationManager : IConversationManager
{
    private readonly ILogger<InMemoryConversationManager> _logger;
    private readonly AgentSettings _settings;
    private readonly ConcurrentDictionary<string, List<ConversationMessage>> _conversations = new();

    public InMemoryConversationManager(ILogger<InMemoryConversationManager> logger, IOptions<AgentConfiguration> options)
    {
        _logger = logger;
        _settings = options.Value.Agent;
    }

    public Task<string> CreateConversationAsync(CancellationToken cancellationToken = default)
    {
        var conversationId = Guid.NewGuid().ToString();
        _conversations[conversationId] = new List<ConversationMessage>();
        
        _logger.LogInformation("Created new conversation {ConversationId}", conversationId);
        return Task.FromResult(conversationId);
    }

    public Task AddMessageAsync(string conversationId, ConversationMessage message, CancellationToken cancellationToken = default)
    {
        if (!_conversations.ContainsKey(conversationId))
        {
            _conversations[conversationId] = new List<ConversationMessage>();
        }

        var messages = _conversations[conversationId];
        lock (messages)
        {
            messages.Add(message);
            
            if (messages.Count > _settings.MaxHistoryLength)
            {
                var toRemove = messages.Count - _settings.MaxHistoryLength;
                messages.RemoveRange(0, toRemove);
                _logger.LogDebug("Trimmed conversation {ConversationId} history, removed {Count} old messages", 
                    conversationId, toRemove);
            }
        }

        _logger.LogDebug("Added message to conversation {ConversationId}: {Role} - {ContentLength} chars", 
            conversationId, message.Role, message.Content.Length);
        
        return Task.CompletedTask;
    }

    public Task<List<ConversationMessage>> GetHistoryAsync(string conversationId, int? limit = null, CancellationToken cancellationToken = default)
    {
        if (!_conversations.TryGetValue(conversationId, out var messages))
        {
            return Task.FromResult(new List<ConversationMessage>());
        }

        lock (messages)
        {
            var result = limit.HasValue ? messages.TakeLast(limit.Value).ToList() : messages.ToList();
            return Task.FromResult(result);
        }
    }

    public Task ClearHistoryAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        if (_conversations.TryGetValue(conversationId, out var messages))
        {
            lock (messages)
            {
                messages.Clear();
            }
            _logger.LogInformation("Cleared conversation history for {ConversationId}", conversationId);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ConversationExistsAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_conversations.ContainsKey(conversationId));
    }

    public Task<List<string>> GetActiveConversationsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_conversations.Keys.ToList());
    }
}