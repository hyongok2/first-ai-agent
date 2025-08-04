using McpAgent.Models;
using McpAgent.Providers;

namespace McpAgent.Services;

public interface IContextManager
{
    Task<OptimizedContext> OptimizeContextAsync(
        List<ConversationMessage> history,
        List<ToolDefinition> availableTools,
        string currentMessage,
        int maxTokens,
        CancellationToken cancellationToken = default);
        
    Task<string> SummarizeHistoryAsync(
        List<ConversationMessage> messages,
        int maxSummaryTokens,
        CancellationToken cancellationToken = default);
}

public class OptimizedContext
{
    public List<ConversationMessage> RecentMessages { get; set; } = new();
    public string? HistorySummary { get; set; }
    public List<ToolDefinition> RelevantTools { get; set; } = new();
    public List<ToolDefinition> AllTools { get; set; } = new();
    public int TokensUsed { get; set; }
    public bool HasSummary { get; set; }
    public string OptimizationStrategy { get; set; } = string.Empty;
}