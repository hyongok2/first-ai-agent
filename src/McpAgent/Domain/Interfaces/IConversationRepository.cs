using McpAgent.Domain.Entities;

namespace McpAgent.Domain.Interfaces;

public interface IConversationRepository
{
    Task<Conversation?> GetByIdAsync(string conversationId, CancellationToken cancellationToken = default);
    Task<Conversation> CreateAsync(string conversationId, CancellationToken cancellationToken = default);
    Task SaveAsync(Conversation conversation, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetActiveConversationIdsAsync(CancellationToken cancellationToken = default);
    Task DeleteAsync(string conversationId, CancellationToken cancellationToken = default);
}