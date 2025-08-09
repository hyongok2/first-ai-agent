using System.Text.Json;
using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;
using McpAgent.Application.Interfaces;
using Microsoft.Extensions.Logging;
using McpAgent.Presentation.Console;

namespace McpAgent.Application.Services;

public class ConversationSummaryService : BaseLlmService<ConversationSummaryService>, IConversationSummaryService
{
    // In-memory storage for conversation summaries
    // In a production environment, this should be replaced with persistent storage
    private readonly Dictionary<string, ConversationSummary> _conversationSummaries = new();

    public ConversationSummaryService(
        ILogger<ConversationSummaryService> logger,
        ILlmProvider llmProvider,
        IPromptService promptService,
        IRequestResponseLogger requestResponseLogger,
        IToolExecutor toolExecutor)
        : base(logger, llmProvider, promptService, requestResponseLogger, toolExecutor)
    {
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
            Logger.LogInformation("Summarizing turn {Turn}", turnNumber);
            
            // 대화 ID 결정 (systemContext에서 추출하거나 기본값 사용)
            var conversationId = ExtractConversationId(systemContext) ?? "default";
            
            // 프롬프트 변수 준비
            var replacements = new Dictionary<string, string>
            {
                {"{SYSTEM_CONTEXT}", systemContext},
                {"{CURRENT_TIME}", GetCurrentTimeInfo()},
                {"{TURN_NUMBER}", turnNumber.ToString()},
                {"{ORIGINAL_INPUT}", originalInput},
                {"{REFINED_INPUT}", refinedInput.RefinedQuery},
                {"{SELECTED_CAPABILITY}", selectedCapability.Type.ToString()},
                {"{TOOL_EXECUTIONS}", FormatToolExecutions(toolExecutions)},
                {"{FINAL_RESPONSE}", finalResponse}
            };

            // 프롬프트 준비 및 LLM 호출
            var prompt = await PreparePromptAsync("conversation-summary", replacements, cancellationToken);

            // Call LLM to summarize the turn using base class method
            var response = await CallLlmAsync(prompt, "ConversationSummary", cancellationToken);

            // Parse the JSON response
            var turnSummary = ParseTurnSummary(response, turnNumber);
            
            // 실제 데이터로 TurnSummary 완성
            turnSummary = new TurnSummary(
                turnNumber,
                originalInput,
                refinedInput.RefinedQuery,
                selectedCapability.Type,
                toolExecutions.ToList(),
                finalResponse,
                turnSummary.OverallSummary
            );
            
            // 대화 요약 업데이트 (자동 저장)
            await UpdateConversationSummaryAsync(conversationId, turnSummary, systemContext, cancellationToken);

            Logger.LogInformation("Turn {Turn} summarized and saved successfully for conversation {ConversationId}", 
                turnNumber, conversationId);

            return turnSummary;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to summarize turn {Turn}",
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

                Logger.LogInformation("Consolidated summary created for conversation {ConversationId}", conversationId);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to consolidate summaries for conversation {ConversationId}", conversationId);
            }
        }

        // LastUpdatedAt is automatically updated in ConversationSummary methods
    }

    private async Task<string> ConsolidateTurnSummariesAsync(
        List<TurnSummary> individualTurns,
        string systemContext,
        CancellationToken cancellationToken = default)
    {
        // 프롬프트 변수 준비
        var replacements = new Dictionary<string, string>
        {
            {"{SYSTEM_CONTEXT}", systemContext},
            {"{CURRENT_TIME}", GetCurrentTimeInfo()},
            {"{INDIVIDUAL_TURN_SUMMARIES}", string.Join('\n', individualTurns.Select(t => $"턴 {t.TurnNumber}: {t.OverallSummary}"))}
        };

        // 프롬프트 준비 및 LLM 호출
        var prompt = await PreparePromptAsync("consolidation-summary", replacements, cancellationToken);
        var response = await CallLlmAsync(prompt, "ConsolidationSummary", cancellationToken);

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

            // 극도로 강화된 요약 형식 파싱
            var comprehensiveSummary = root.TryGetProperty("comprehensive_summary", out var compSummary)
                ? compSummary.GetString() ?? "" : "";
                
            // 기존 호환성을 위해 overall_summary도 체크
            if (string.IsNullOrEmpty(comprehensiveSummary))
            {
                comprehensiveSummary = root.TryGetProperty("overall_summary", out var summary)
                    ? summary.GetString() ?? "" : "";
            }
            
            // preserved_technical_data 섭션 파싱
            var technicalData = "";
            if (root.TryGetProperty("preserved_technical_data", out var techElement))
            {
                var techParts = new List<string>();
                
                if (techElement.TryGetProperty("file_paths_and_directories", out var filePaths))
                    techParts.Add($"파일 경로: {filePaths.GetString()}");
                    
                if (techElement.TryGetProperty("commands_and_parameters", out var commands))
                    techParts.Add($"명령어: {commands.GetString()}");
                    
                if (techElement.TryGetProperty("configuration_values", out var config))
                    techParts.Add($"설정값: {config.GetString()}");
                    
                if (techElement.TryGetProperty("error_messages_and_codes", out var errors))
                    techParts.Add($"에러 메시지: {errors.GetString()}");
                    
                if (techElement.TryGetProperty("numerical_data", out var numbers))
                    techParts.Add($"수치 데이터: {numbers.GetString()}");
                    
                if (techElement.TryGetProperty("urls_and_endpoints", out var urls))
                    techParts.Add($"URL/엔드포인트: {urls.GetString()}");
                    
                technicalData = string.Join(" | ", techParts);
            }
            
            // complete_context_record 섭션 파싱
            var completeContext = "";
            if (root.TryGetProperty("complete_context_record", out var contextElement))
            {
                var contextParts = new List<string>();
                
                if (contextElement.TryGetProperty("user_request_details", out var userDetails))
                    contextParts.Add($"요청 상세: {userDetails.GetString()}");
                    
                if (contextElement.TryGetProperty("system_actions_taken", out var systemActions))
                    contextParts.Add($"시스템 작업: {systemActions.GetString()}");
                    
                if (contextElement.TryGetProperty("results_and_data", out var results))
                    contextParts.Add($"결과 데이터: {results.GetString()}");
                    
                if (contextElement.TryGetProperty("pending_tasks", out var pending))
                    contextParts.Add($"미완료 작업: {pending.GetString()}");
                    
                if (contextElement.TryGetProperty("context_variables", out var variables))
                    contextParts.Add($"컨텍스트 변수: {variables.GetString()}");
                    
                completeContext = string.Join(" | ", contextParts);
            }
            
            // critical_continuity_info 파싱
            var keyInfo = new List<string>();
            if (root.TryGetProperty("critical_continuity_info", out var keyInfoArray))
            {
                foreach (var item in keyInfoArray.EnumerateArray())
                {
                    var info = item.GetString();
                    if (!string.IsNullOrEmpty(info))
                        keyInfo.Add(info);
                }
            }
            
            // 기존 호환성을 위해 key_information_for_continuity도 체크
            if (!keyInfo.Any() && root.TryGetProperty("key_information_for_continuity", out var altKeyInfoArray))
            {
                foreach (var item in keyInfoArray.EnumerateArray())
                {
                    var info = item.GetString();
                    if (!string.IsNullOrEmpty(info))
                        keyInfo.Add(info);
                }
            }
            
            var userIntent = root.TryGetProperty("user_ultimate_goal", out var intent)
                ? intent.GetString() ?? "" : "";
            
            // 기존 호환성
            if (string.IsNullOrEmpty(userIntent))
            {
                userIntent = root.TryGetProperty("user_intent", out var altIntent)
                    ? altIntent.GetString() ?? "" : "";
            }
                
            var systemAction = root.TryGetProperty("system_performed_actions", out var action)
                ? action.GetString() ?? "" : "";
                
            // 기존 호환성
            if (string.IsNullOrEmpty(systemAction))
            {
                systemAction = root.TryGetProperty("system_action", out var altAction)
                    ? altAction.GetString() ?? "" : "";
            }
                
            var outcome = root.TryGetProperty("current_status", out var resultOutcome)
                ? resultOutcome.GetString() ?? "" : "";
                
            // 기존 호환성
            if (string.IsNullOrEmpty(outcome))
            {
                outcome = root.TryGetProperty("outcome", out var altOutcome)
                    ? altOutcome.GetString() ?? "" : "";
            }
                
            var nextActions = root.TryGetProperty("expected_next_steps", out var next)
                ? next.GetString() ?? "" : "";
                
            // 기존 호환성
            if (string.IsNullOrEmpty(nextActions))
            {
                nextActions = root.TryGetProperty("next_expected_actions", out var altNext)
                    ? altNext.GetString() ?? "" : "";
            }

            // 극도로 상세한 맥락 요약 생성
            var finalSummary = comprehensiveSummary;
            
            if (!string.IsNullOrEmpty(technicalData))
                finalSummary += $"\n\n기술적 데이터: {technicalData}";
                
            if (!string.IsNullOrEmpty(completeContext))
                finalSummary += $"\n\n상세 컨텍스트: {completeContext}";
                
            if (keyInfo.Any())
                finalSummary += $"\n\n핵심 정보: {string.Join("; ", keyInfo)}";
                
            if (!string.IsNullOrEmpty(userIntent))
                finalSummary += $"\n\n사용자 또드 또는 의도: {userIntent}";
                
            if (!string.IsNullOrEmpty(systemAction))
                finalSummary += $"\n\n시스템 수행 행동: {systemAction}";
                
            if (!string.IsNullOrEmpty(outcome))
                finalSummary += $"\n\n현재 상태 및 결과: {outcome}";
                
            if (!string.IsNullOrEmpty(nextActions))
                finalSummary += $"\n\n다음 단계 예상: {nextActions}";

            return new TurnSummary(
                turnNumber,
                "", // UserInput - will be filled by caller
                "", // RefinedInput - will be filled by caller  
                SystemCapabilityType.SimpleChat, // Default capability
                new List<ToolExecution>(), // Empty tool executions
                "", // FinalResponse - will be filled by caller
                finalSummary
            );
        }
        catch (JsonException ex)
        {
            Logger.LogError(ex, "Failed to parse turn summary JSON: {Response}", response);
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
            Logger.LogError(ex, "Failed to parse consolidation result JSON: {Response}", response);
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


    private TurnSummary CreateFallbackTurnSummary(int turnNumber, string originalInput, string finalResponse)
    {
        Logger.LogWarning("Creating fallback turn summary for turn {Turn}", turnNumber);

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
    
    /// <summary>
    /// 대화 요약을 강제로 저장합니다. (요청 완료 시 호출)
    /// </summary>
    public async Task SaveConversationTurnAsync(
        string conversationId,
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
            var turnSummary = await SummarizeTurnAsync(
                turnNumber,
                originalInput,
                refinedInput,
                selectedCapability,
                toolExecutions,
                finalResponse,
                systemContext,
                cancellationToken);
            
            Logger.LogInformation("Conversation turn saved for {ConversationId}, turn {Turn}", 
                conversationId, turnNumber);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to save conversation turn for {ConversationId}", conversationId);
        }
    }
    
    private string? ExtractConversationId(string systemContext)
    {
        // systemContext에서 대화 ID를 추출하는 로직
        // 예: "AI 에이전트 - 대화ID: abc123" -> "abc123"
        if (string.IsNullOrEmpty(systemContext))
            return null;
            
        var match = System.Text.RegularExpressions.Regex.Match(systemContext, @"대화ID:\s*([\w-]+)");
        return match.Success ? match.Groups[1].Value : null;
    }
}