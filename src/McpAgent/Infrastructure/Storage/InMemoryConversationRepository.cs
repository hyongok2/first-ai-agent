using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;
using System.Collections.Concurrent;

namespace McpAgent.Infrastructure.Storage;

public class InMemoryConversationRepository : IConversationRepository
{
    private readonly ConcurrentDictionary<string, Conversation> _conversations = new();

    public Task<Conversation?> GetByIdAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        _conversations.TryGetValue(conversationId, out var conversation);
        return Task.FromResult(conversation);
    }

    public Task<Conversation> CreateAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        var conversation = new Conversation(conversationId);
        _conversations.TryAdd(conversationId, conversation);
        return Task.FromResult(conversation);
    }

    public Task SaveAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        _conversations.AddOrUpdate(conversation.Id, conversation, (_, _) => conversation);
        return Task.CompletedTask;
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
        _conversations.TryRemove(conversationId, out _);
        return Task.CompletedTask;
    }
}