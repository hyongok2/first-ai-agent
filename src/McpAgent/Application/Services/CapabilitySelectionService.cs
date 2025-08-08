using System.Text.Json;
using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;
using McpAgent.Application.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using McpAgent.Presentation.Console;

namespace McpAgent.Application.Services;

public class CapabilitySelectionService : ICapabilitySelectionService
{
    private readonly ILlmProvider _llmProvider;
    private readonly IPromptService _promptService;
    private readonly ILogger<CapabilitySelectionService> _logger;
    private readonly IRequestResponseLogger _requestResponseLogger;
    private readonly IToolExecutor _toolExecutor;
    private readonly ConsoleUIService _consoleUIService;

    public CapabilitySelectionService(
        ILlmProvider llmProvider,
        IPromptService promptService,
        ILogger<CapabilitySelectionService> logger,
        IRequestResponseLogger requestResponseLogger,
        IToolExecutor toolExecutor, ConsoleUIService consoleUIService)
    {
        _llmProvider = llmProvider;
        _promptService = promptService;
        _logger = logger;
        _requestResponseLogger = requestResponseLogger;
        _toolExecutor = toolExecutor;
        _consoleUIService = consoleUIService;
    }

    public async Task<SystemCapability> SelectCapabilityAsync(
        RefinedInput refinedInput,
        IReadOnlyList<ConversationMessage> conversationHistory,
        string systemContext,
        IReadOnlyList<SystemCapabilityType> availableCapabilities,
        IReadOnlyList<ToolExecution>? toolExecutionResults = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Selecting capability for intent: {Intent}", refinedInput.ClarifiedIntent);
            
            _consoleUIService.DisplayProcess("사용할 기능을 선택 중입니다...");

            // Load the capability selection prompt template
            var promptTemplate = await _promptService.GetPromptAsync("capability-selection");
            
            // Prepare available capabilities description
            var availableCapabilitiesText = GetAvailableCapabilitiesDescription();
            var conversationHistoryText = FormatConversationHistory(conversationHistory);
            var toolResultsText = FormatToolExecutionResults(toolExecutionResults);
            var availableMcpToolsText = await GetAvailableMcpToolsDescriptionAsync(cancellationToken);
            var currentTimeText = GetCurrentTimeInfo();
            
            // Replace placeholders in the template
            var prompt = promptTemplate
                .Replace("{SYSTEM_CONTEXT}", systemContext)
                .Replace("{CURRENT_TIME}", currentTimeText)
                .Replace("{AVAILABLE_CAPABILITIES}", availableCapabilitiesText)
                .Replace("{AVAILABLE_MCP_TOOLS}", availableMcpToolsText)
                .Replace("{CONVERSATION_HISTORY}", conversationHistoryText)
                .Replace("{CLARIFIED_INTENT}", refinedInput.ClarifiedIntent)
                .Replace("{REFINED_QUERY}", refinedInput.RefinedQuery)
                .Replace("{SUGGESTED_PLAN}", refinedInput.SuggestedPlan ?? "특별한 계획이 제안되지 않았습니다.")
                .Replace("{CUMULATIVE_PLANS}", ExtractCumulativePlansFromContext(systemContext))
                .Replace("{CONFIDENCE_LEVEL}", refinedInput.IntentConfidence.ToString())
                .Replace("{TOOL_EXECUTION_RESULTS}", toolResultsText);

            Stopwatch stopwatch = Stopwatch.StartNew();
            
            // Call LLM to select capability
            var response = await _llmProvider.GenerateResponseAsync(prompt, cancellationToken);

            stopwatch.Stop();

            // LLM 요청/응답 로깅
            _ = Task.Run(() => _requestResponseLogger.LogLlmRequestResponseAsync(
                _llmProvider.GetLlmModel(), "CapabilitySelection", prompt, response,stopwatch.Elapsed.TotalMilliseconds, cancellationToken));
            
            // Parse the JSON response
            var selectedCapability = ParseCapabilitySelection(response);
            
            _logger.LogInformation("Selected capability: {Capability}", selectedCapability.Type);
            
            return selectedCapability;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to select capability for intent: {Intent}", refinedInput.ClarifiedIntent);
            
            // Return fallback capability selection
            return CreateFallbackCapabilitySelection(refinedInput);
        }
    }

    public async Task<IReadOnlyList<SystemCapabilityType>> GetAvailableCapabilitiesAsync()
    {
        // Return all available capability types
        var capabilities = Enum.GetValues<SystemCapabilityType>().ToList();
        return capabilities.AsReadOnly();
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

    private string GetAvailableCapabilitiesDescription()
    {
        return @"
1. **IntentClarification** - 사용자 의도가 불분명하여 명확화가 필요한 경우
   - 사용 시기: 요청이 모호하거나 추가 정보가 필요할 때
   - 예시: 불완전한 질문, 컨텍스트가 부족한 요청

2. **SimpleChat** - 일반적인 대화 응답 (도구 사용 없음)
   - 사용 시기: 인사, 감사, 일반 질문 등 간단한 대화
   - 예시: '안녕하세요', '고마워요', '날씨가 어때요?'

3. **TaskCompletion** - 사용자 요청이 완료되어 최종 응답하는 경우
   - 사용 시기: 이전 단계에서 충분한 정보를 수집한 후 최종 답변
   - 예시: 질문에 대한 완전한 답변, 작업 완료 보고

4. **InternalTool** - 내부 시스템 도구 사용 (현재 사용 불가)
   - 상태: 비활성화됨

5. **McpTool** - MCP 외부 도구 사용
   - 사용 시기: 파일 시스템, 웹 검색, API 호출 등 외부 도구가 필요할 때
   - 예시: 파일 읽기/쓰기, 웹 페이지 검색, 데이터베이스 조회

6. **TaskPlanning** - 복잡한 작업을 위한 계획 수립
   - 사용 시기: 다단계 작업이나 복잡한 프로세스가 필요할 때
   - 예시: 프로젝트 계획, 여러 단계의 분석 작업

7. **ErrorHandling** - 오류 또는 예외 상황 처리
   - 사용 시기: 이전 작업에서 오류가 발생했거나 예외 상황 처리가 필요할 때
   - 예시: 도구 실행 실패, 잘못된 입력, 시스템 오류
        ";
    }

    private SystemCapability ParseCapabilitySelection(string response)
    {
        try
        {
            // Extract JSON from response if it's wrapped in markdown
            var jsonResponse = ExtractJsonFromResponse(response);
            
            var jsonDocument = JsonDocument.Parse(jsonResponse);
            var root = jsonDocument.RootElement;

            var capabilityTypeString = root.TryGetProperty("selected_capability", out var capabilityElement)
                ? capabilityElement.GetString() ?? "SimpleChat" : "SimpleChat";

            // Parse capability type enum
            if (!Enum.TryParse<SystemCapabilityType>(capabilityTypeString, out var capabilityType))
            {
                _logger.LogWarning("Unknown capability type: {Type}, defaulting to SimpleChat", capabilityTypeString);
                capabilityType = SystemCapabilityType.SimpleChat;
            }

            var description = root.TryGetProperty("description", out var descriptionElement) 
                ? descriptionElement.GetString() ?? "" : "";
            var reasoning = root.TryGetProperty("reasoning", out var reasoningElement) 
                ? reasoningElement.GetString() ?? "" : "";
            var parameters = ParseParameters(root);

            return new SystemCapability(capabilityType, description, reasoning, parameters);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse capability selection JSON: {Response}", response);
            throw new InvalidOperationException("Failed to parse LLM response as JSON", ex);
        }
    }

    private Dictionary<string, object> ParseParameters(JsonElement root)
    {
        var parameters = new Dictionary<string, object>();
        
        if (root.TryGetProperty("parameters", out var parametersElement))
        {
            foreach (var property in parametersElement.EnumerateObject())
            {
                var value = (object)(property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString() ?? "",
                    JsonValueKind.Number => property.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => property.Value.GetRawText()
                });
                parameters[property.Name] = value;
            }
        }
        
        return parameters;
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

    private SystemCapability CreateFallbackCapabilitySelection(RefinedInput refinedInput)
    {
        _logger.LogWarning("Creating fallback capability selection for: {Intent}", refinedInput.ClarifiedIntent);
        
        // Use simple heuristics to determine fallback capability
        var capabilityType = DetermineHeuristicCapability(refinedInput);
        
        return new SystemCapability(
            capabilityType, 
            "자동 선택된 대안 기능",
            "LLM 응답 실패로 인한 휴리스틱 기반 선택", 
            new Dictionary<string, object>());
    }

    private SystemCapabilityType DetermineHeuristicCapability(RefinedInput refinedInput)
    {
        var input = refinedInput.ClarifiedIntent.ToLowerInvariant();
        
        // Simple keyword-based heuristics
        if (input.Contains("파일") || input.Contains("웹") || input.Contains("검색") || input.Contains("api"))
        {
            return SystemCapabilityType.McpTool;
        }
        
        if (refinedInput.RequiresFollowUp || refinedInput.IntentConfidence < ConfidenceLevel.Medium)
        {
            return SystemCapabilityType.IntentClarification;
        }
        
        if (input.Contains("계획") || input.Contains("단계") || input.Contains("프로젝트"))
        {
            return SystemCapabilityType.TaskPlanning;
        }
        
        // Default to simple chat
        return SystemCapabilityType.SimpleChat;
    }

    private string FormatConversationHistory(IReadOnlyList<ConversationMessage> conversationHistory)
    {
        if (conversationHistory == null || conversationHistory.Count == 0)
        {
            return "대화 이력이 없습니다.";
        }

        var history = conversationHistory
            .Select(msg => $"{msg.Role}: {msg.Content}")
            .ToList();

        return string.Join('\n', history);
    }

    private string FormatToolExecutionResults(IReadOnlyList<ToolExecution>? toolExecutionResults)
    {
        if (toolExecutionResults == null || toolExecutionResults.Count == 0)
        {
            return "이전 도구 실행 결과가 없습니다.";
        }

        var results = toolExecutionResults
            .Select(result => $"도구: {result.ToolName}, 결과: {result.Result}")
            .ToList();

        return string.Join('\n', results);
    }

    private static string ExtractCumulativePlansFromContext(string systemContext)
    {
        // systemContext에서 [진행 계획 상태] 섹션 추출
        var startMarker = "[진행 계획 상태]";
        var startIndex = systemContext.IndexOf(startMarker);
        
        if (startIndex == -1)
            return "아직 제안된 계획이 없습니다.";
        
        var plansSection = systemContext.Substring(startIndex + startMarker.Length).Trim();
        return string.IsNullOrEmpty(plansSection) ? "아직 제안된 계획이 없습니다." : plansSection;
    }

    private async Task<string> GetAvailableMcpToolsDescriptionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var availableTools = await _toolExecutor.GetAvailableToolsAsync(cancellationToken);
            
            if (availableTools == null || availableTools.Count == 0)
            {
                return "현재 사용 가능한 MCP 도구가 없습니다.";
            }

            var toolDescriptions = availableTools
                .Select(tool => $"- **{tool.Name}**: {tool.Description}")
                .ToList();

            var header = $"=== 사용 가능한 MCP 도구 ({availableTools.Count}개) ===";
            var toolList = string.Join('\n', toolDescriptions);
            
            return $"{header}\n{toolList}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get available MCP tools information");
            return "MCP 도구 정보를 가져올 수 없습니다.";
        }
    }
}