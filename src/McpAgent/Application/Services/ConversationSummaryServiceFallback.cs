using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace McpAgent.Application.Services;

/// <summary>
/// Simplified ConversationSummaryService with working fallback implementations
/// </summary>
public class ConversationSummaryServiceFallback : IConversationSummaryService
{
    private readonly ILogger<ConversationSummaryServiceFallback> _logger;
    private readonly Dictionary<string, ConversationSummary> _conversationSummaries = new();

    public ConversationSummaryServiceFallback(ILogger<ConversationSummaryServiceFallback> logger)
    {
        _logger = logger;
    }

    public async Task<TurnSummary> SummarizeTurnAsync(
        int turnNumber,
        string originalInput,
        RefinedInput refinedInput,
        SystemCapability selectedCapability,
        IReadOnlyList<ToolExecution> toolExecutions,
        string finalResponse,
        string systemContext,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Summarizing turn {Turn}", turnNumber);

            // Create simplified turn summary
            var turnSummary = new TurnSummary(
                turnNumber,
                originalInput,
                refinedInput.RefinedQuery,
                selectedCapability.Type,
                toolExecutions?.ToList(),
                finalResponse,
                $"턴 {turnNumber}: {refinedInput.ClarifiedIntent} -> {selectedCapability.Type}"
            );

            _logger.LogInformation("Turn {Turn} summarized successfully", turnNumber);
            
            return turnSummary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to summarize turn {Turn}", turnNumber);
            
            // Return minimal fallback turn summary
            return new TurnSummary(
                turnNumber,
                originalInput,
                refinedInput?.RefinedQuery ?? originalInput,
                selectedCapability?.Type ?? SystemCapabilityType.SimpleChat,
                toolExecutions?.ToList(),
                finalResponse ?? "응답 생성 실패",
                $"턴 {turnNumber} 요약 (오류)"
            );
        }
    }

    public async Task<string> ConsolidateTurnsAsync(
        IReadOnlyList<TurnSummary> individualTurns,
        string systemContext,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (individualTurns == null || !individualTurns.Any())
            {
                return "통합할 대화 이력이 없습니다.";
            }

            var consolidated = string.Join(" ", individualTurns.Select(t => t.OverallSummary));
            return $"통합 요약 ({individualTurns.Count}개 턴): {consolidated}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to consolidate turns");
            return "통합 요약 생성 실패";
        }
    }

    public async Task<ConversationSummary> GetConversationSummaryAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        _conversationSummaries.TryGetValue(conversationId, out var summary);
        return summary ?? new ConversationSummary(conversationId);
    }

    public async Task<string> GetConversationHistoryAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        var summary = await GetConversationSummaryAsync(conversationId, cancellationToken);
        
        if (summary.TotalTurns == 0)
        {
            return "새로운 대화 시작";
        }

        return summary.GetContextForNewTurn();
    }
}