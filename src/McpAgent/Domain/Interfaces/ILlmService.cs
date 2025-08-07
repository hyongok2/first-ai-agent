using McpAgent.Domain.Entities;

namespace McpAgent.Domain.Interfaces;

public interface ILlmService
{
    Task<string> GenerateResponseAsync(
        string prompt, 
        IReadOnlyList<ConversationMessage> history,
        CancellationToken cancellationToken = default);
    
    Task<string> GenerateResponseAsync(
        string prompt,
        IReadOnlyList<ConversationMessage> history,
        IReadOnlyList<ToolDefinition> availableTools,
        CancellationToken cancellationToken = default);
    
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}