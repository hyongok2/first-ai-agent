using McpAgent.Models;

namespace McpAgent.Memory;

public interface IConversationManager
{
    Task<string> CreateConversationAsync(CancellationToken cancellationToken = default);
    Task AddMessageAsync(string conversationId, ConversationMessage message, CancellationToken cancellationToken = default);
    Task<List<ConversationMessage>> GetHistoryAsync(string conversationId, int? limit = null, CancellationToken cancellationToken = default);
    Task ClearHistoryAsync(string conversationId, CancellationToken cancellationToken = default);
    Task<bool> ConversationExistsAsync(string conversationId, CancellationToken cancellationToken = default);
    Task<List<string>> GetActiveConversationsAsync(CancellationToken cancellationToken = default);
}