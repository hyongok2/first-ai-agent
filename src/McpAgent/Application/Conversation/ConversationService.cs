using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace McpAgent.Application.Conversation;

public class ConversationService
{
    private readonly ILogger<ConversationService> _logger;
    private readonly IConversationRepository _repository;

    public ConversationService(ILogger<ConversationService> logger, IConversationRepository repository)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<Domain.Entities.Conversation> GetOrCreateConversationAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        var conversation = await _repository.GetByIdAsync(conversationId, cancellationToken);
        
        if (conversation == null)
        {
            _logger.LogInformation("Creating new conversation with ID: {ConversationId}", conversationId);
            conversation = await _repository.CreateAsync(conversationId, cancellationToken);
        }

        return conversation;
    }

    public async Task<IReadOnlyList<string>> GetActiveConversationsAsync(CancellationToken cancellationToken = default)
    {
        return await _repository.GetActiveConversationIdsAsync(cancellationToken);
    }

    public async Task SaveConversationAsync(Domain.Entities.Conversation conversation, CancellationToken cancellationToken = default)
    {
        await _repository.SaveAsync(conversation, cancellationToken);
        _logger.LogDebug("Conversation {ConversationId} saved successfully", conversation.Id);
    }
}