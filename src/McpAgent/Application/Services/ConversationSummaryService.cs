using System.Text.Json;
using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;
using McpAgent.Application.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace McpAgent.Application.Services;

public class ConversationSummaryService : IConversationSummaryService
{
    private readonly ILlmProvider _llmProvider;
    private readonly IPromptService _promptService;
    private readonly ILogger<ConversationSummaryService> _logger;
    private readonly IRequestResponseLogger _requestResponseLogger;
    private readonly IToolExecutor _toolExecutor;

    // In-memory storage for conversation summaries
    // In a production environment, this should be replaced with persistent storage
    private readonly Dictionary<string, ConversationSummary> _conversationSummaries = new();

    public ConversationSummaryService(
        ILlmProvider llmProvider,
        IPromptService promptService,
        ILogger<ConversationSummaryService> logger,
        IRequestResponseLogger requestResponseLogger,
        IToolExecutor toolExecutor)
    {
        _llmProvider = llmProvider;
        _promptService = promptService;
        _logger = logger;
        _requestResponseLogger = requestResponseLogger;
        _toolExecutor = toolExecutor;
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

            // Load the conversation summary prompt template
            var promptTemplate = await _promptService.GetPromptAsync("conversation-summary");

            // Format complex objects
            var toolExecutionsText = FormatToolExecutions(toolExecutions);
            var currentTimeText = GetCurrentTimeInfo();

            // Replace placeholders in the template
            var prompt = promptTemplate
                .Replace("{SYSTEM_CONTEXT}", systemContext)
                .Replace("{CURRENT_TIME}", currentTimeText)
                .Replace("{TURN_NUMBER}", turnNumber.ToString())
                .Replace("{ORIGINAL_INPUT}", originalInput)
                .Replace("{REFINED_INPUT}", refinedInput.RefinedQuery)
                .Replace("{SELECTED_CAPABILITY}", selectedCapability.Type.ToString())
                .Replace("{TOOL_EXECUTIONS}", toolExecutionsText)
                .Replace("{FINAL_RESPONSE}", finalResponse);

            Stopwatch stopwatch = Stopwatch.StartNew();

            // Call LLM to summarize the turn
            var response = await _llmProvider.GenerateResponseAsync(prompt, cancellationToken);

            stopwatch.Stop();

            // LLM 요청/응답 로깅
            _ = Task.Run(() => _requestResponseLogger.LogLlmRequestResponseAsync(
                "qwen3:32b", "ConversationSummary", prompt, response, stopwatch.Elapsed.TotalMilliseconds, cancellationToken));

            // Parse the JSON response
            var turnSummary = ParseTurnSummary(response, turnNumber);

            _logger.LogInformation("Turn {Turn} summarized successfully", turnNumber);

            return turnSummary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to summarize turn {Turn}",
                turnNumber);

            // Return fallback turn summary
            return CreateFallbackTurnSummary(turnNumber, originalInput, finalResponse);
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
            return "대화 이력이 없습니다.";
        }

        var history = new List<string>();

        // Add consolidated summary if available
        if (!string.IsNullOrEmpty(summary.ConsolidatedSummary))
        {
            history.Add($"=== 이전 대화 통합 요약 ({summary.TotalTurns - summary.IndividualTurns.Count}턴) ===");
            history.Add(summary.ConsolidatedSummary);
            history.Add("");
        }

        // Add individual turn summaries (last 5 or fewer)
        if (summary.IndividualTurns.Any())
        {
            history.Add($"=== 최근 개별 턴 요약 ({summary.IndividualTurns.Count}턴) ===");

            foreach (var turn in summary.IndividualTurns.OrderBy(t => t.TurnNumber))
            {
                history.Add($"턴 {turn.TurnNumber}: {turn.OverallSummary}");
            }
        }

        return string.Join('\n', history);
    }

    public async Task<string> ConsolidateTurnsAsync(
        IReadOnlyList<TurnSummary> individualTurns,
        string systemContext,
        CancellationToken cancellationToken = default)
    {
        return await ConsolidateTurnSummariesAsync(individualTurns.ToList(), systemContext, cancellationToken);
    }

    private string FormatToolExecutions(IReadOnlyList<ToolExecution> toolExecutions)
    {
        if (toolExecutions == null || toolExecutions.Count == 0)
        {
            return "실행된 도구가 없습니다.";
        }

        var executions = toolExecutions
            .Select(exec => $"도구: {exec.ToolName}, 성공: {exec.IsSuccess}, 결과: {exec.Result}")
            .ToList();

        return string.Join('\n', executions);
    }

    private string GetCurrentTimeInfo()
    {
        var now = DateTime.Now;
        var utcNow = DateTime.UtcNow;

        return $@"현재 시간: {now:yyyy-MM-dd HH:mm:ss} (현지 시간)
UTC 시간: {utcNow:yyyy-MM-dd HH:mm:ss}
요일: {now.DayOfWeek switch
        {
            DayOfWeek.Monday => "월요일",
            DayOfWeek.Tuesday => "화요일",
            DayOfWeek.Wednesday => "수요일",
            DayOfWeek.Thursday => "목요일",
            DayOfWeek.Friday => "금요일",
            DayOfWeek.Saturday => "토요일",
            DayOfWeek.Sunday => "일요일",
            _ => now.DayOfWeek.ToString()
        }}
타임존: {TimeZoneInfo.Local.DisplayName}";
    }

    private async Task UpdateConversationSummaryAsync(
        string conversationId,
        TurnSummary turnSummary,
        string systemContext,
        CancellationToken cancellationToken = default)
    {
        if (!_conversationSummaries.TryGetValue(conversationId, out var conversationSummary))
        {
            conversationSummary = new ConversationSummary(conversationId);
            _conversationSummaries[conversationId] = conversationSummary;
        }

        // Add the new turn summary
        conversationSummary.AddTurnSummary(turnSummary);

        // If we've reached 5 individual turns, consolidate them
        if (conversationSummary.IndividualTurns.Count == 5 && conversationSummary.TotalTurns == 5)
        {
            try
            {
                var consolidatedSummary = await ConsolidateTurnSummariesAsync(
                    conversationSummary.IndividualTurns, systemContext, cancellationToken);

                conversationSummary.SetConsolidatedSummary(consolidatedSummary);

                _logger.LogInformation("Consolidated summary created for conversation {ConversationId}", conversationId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to consolidate summaries for conversation {ConversationId}", conversationId);
            }
        }

        // LastUpdatedAt is automatically updated in ConversationSummary methods
    }

    private async Task<string> ConsolidateTurnSummariesAsync(
        List<TurnSummary> individualTurns,
        string systemContext,
        CancellationToken cancellationToken = default)
    {
        // Load the consolidation summary prompt template
        var promptTemplate = await _promptService.GetPromptAsync("consolidation-summary");

        // Prepare individual turn summaries text
        var turnSummariesText = string.Join('\n',
            individualTurns.Select(t => $"턴 {t.TurnNumber}: {t.OverallSummary}"));
        var currentTimeText = GetCurrentTimeInfo();

        // Replace placeholders in the template
        var prompt = promptTemplate
            .Replace("{SYSTEM_CONTEXT}", systemContext)
            .Replace("{CURRENT_TIME}", currentTimeText)
            .Replace("{INDIVIDUAL_TURN_SUMMARIES}", turnSummariesText);
        Stopwatch stopwatch = Stopwatch.StartNew();
        // Call LLM to consolidate summaries
        var response = await _llmProvider.GenerateResponseAsync(prompt, cancellationToken);
        stopwatch.Stop();
        // LLM 요청/응답 로깅
        _ = Task.Run(() => _requestResponseLogger.LogLlmRequestResponseAsync(
            _llmProvider.GetLlmModel(), "ConsolidationSummary", prompt, response,stopwatch.ElapsedMilliseconds, cancellationToken));

        // Parse the JSON response and extract consolidated summary
        var consolidationResult = ParseConsolidationResult(response);

        return consolidationResult;
    }

    private TurnSummary ParseTurnSummary(string response, int turnNumber)
    {
        try
        {
            // Extract JSON from response if it's wrapped in markdown
            var jsonResponse = ExtractJsonFromResponse(response);

            var jsonDocument = JsonDocument.Parse(jsonResponse);
            var root = jsonDocument.RootElement;

            // Parse the response and create a comprehensive summary string
            var overallSummary = root.TryGetProperty("overall_summary", out var summary)
                ? summary.GetString() ?? "" : "";
            var userIntent = root.TryGetProperty("user_intent", out var intent)
                ? intent.GetString() ?? "" : "";
            var systemAction = root.TryGetProperty("system_action", out var action)
                ? action.GetString() ?? "" : "";
            var outcome = root.TryGetProperty("outcome", out var resultOutcome)
                ? resultOutcome.GetString() ?? "" : "";

            // Combine all analysis into a comprehensive summary
            var comprehensiveSummary = $"{overallSummary}";
            if (!string.IsNullOrEmpty(userIntent))
                comprehensiveSummary += $" | 사용자 의도: {userIntent}";
            if (!string.IsNullOrEmpty(systemAction))
                comprehensiveSummary += $" | 시스템 액션: {systemAction}";
            if (!string.IsNullOrEmpty(outcome))
                comprehensiveSummary += $" | 결과: {outcome}";

            return new TurnSummary(
                turnNumber,
                "", // UserInput - will be filled by caller
                "", // RefinedInput - will be filled by caller  
                SystemCapabilityType.SimpleChat, // Default capability
                new List<ToolExecution>(), // Empty tool executions
                "", // FinalResponse - will be filled by caller
                comprehensiveSummary
            );
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse turn summary JSON: {Response}", response);
            throw new InvalidOperationException("Failed to parse LLM response as JSON", ex);
        }
    }

    private string ParseConsolidationResult(string response)
    {
        try
        {
            // Extract JSON from response if it's wrapped in markdown
            var jsonResponse = ExtractJsonFromResponse(response);

            var jsonDocument = JsonDocument.Parse(jsonResponse);
            var root = jsonDocument.RootElement;

            return root.TryGetProperty("consolidated_summary", out var summary)
                ? summary.GetString() ?? "" : "";
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse consolidation result JSON: {Response}", response);
            return "통합 요약 생성에 실패했습니다.";
        }
    }

    private List<string> ParseStringArray(JsonElement root, string propertyName)
    {
        var result = new List<string>();

        if (root.TryGetProperty(propertyName, out var arrayElement) &&
            arrayElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arrayElement.EnumerateArray())
            {
                var text = item.GetString();
                if (!string.IsNullOrEmpty(text))
                {
                    result.Add(text);
                }
            }
        }

        return result;
    }

    private string ExtractJsonFromResponse(string response)
    {
        // Remove markdown code blocks if present
        var lines = response.Split('\n');
        var jsonStartIndex = -1;
        var jsonEndIndex = -1;

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim().StartsWith("```json"))
            {
                jsonStartIndex = i + 1;
            }
            else if (lines[i].Trim() == "```" && jsonStartIndex != -1)
            {
                jsonEndIndex = i;
                break;
            }
        }

        if (jsonStartIndex != -1 && jsonEndIndex != -1)
        {
            return string.Join('\n', lines[jsonStartIndex..jsonEndIndex]);
        }

        // If no markdown blocks found, assume entire response is JSON
        return response.Trim();
    }

    private TurnSummary CreateFallbackTurnSummary(int turnNumber, string originalInput, string finalResponse)
    {
        _logger.LogWarning("Creating fallback turn summary for turn {Turn}", turnNumber);

        return new TurnSummary(
            turnNumber,
            originalInput,
            originalInput, // Use original as refined for fallback
            SystemCapabilityType.ErrorHandling,
            new List<ToolExecution>(),
            finalResponse,
            $"턴 {turnNumber} 폴백 요약 - 입력: {originalInput.Substring(0, Math.Min(50, originalInput.Length))}... 응답: {finalResponse.Substring(0, Math.Min(50, finalResponse.Length))}..."
        );
    }
}