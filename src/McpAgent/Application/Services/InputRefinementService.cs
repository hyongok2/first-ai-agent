using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;
using McpAgent.Application.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Diagnostics;
using McpAgent.Presentation.Console;

namespace McpAgent.Application.Services;

/// <summary>
/// LLM 기반 InputRefinementService - input-refinement.txt 프롬프트 사용
/// </summary>
public class InputRefinementService : IInputRefinementService
{
    private readonly ILogger<InputRefinementService> _logger;
    private readonly ILlmProvider _llmProvider;
    private readonly IPromptService _promptService;
    private readonly IRequestResponseLogger _requestResponseLogger;
    private readonly IToolExecutor _toolExecutor;
    private readonly ConsoleUIService _consoleUIService;

    public InputRefinementService(
        ILogger<InputRefinementService> logger,
        ILlmProvider llmProvider,
        IPromptService promptService,
        IRequestResponseLogger requestResponseLogger,
        IToolExecutor toolExecutor,
        ConsoleUIService consoleUIService)
    {
        _logger = logger;
        _llmProvider = llmProvider;
        _promptService = promptService;
        _requestResponseLogger = requestResponseLogger;
        _toolExecutor = toolExecutor;
        _consoleUIService = consoleUIService;
    }

    public async Task<RefinedInput> RefineInputAsync(
        string originalInput,
        IReadOnlyList<ConversationMessage> conversationHistory,
        string systemContext,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Refining input using LLM with input-refinement prompt: {Input}", originalInput);
            _consoleUIService.DisplayProcess("사용자의 의도를 파악 중입니다...");
            // input-refinement.txt 프롬프트 로드
            var promptTemplate = await _promptService.GetPromptAsync("input-refinement", cancellationToken);

            // 대화 이력 포맷팅
            var historyText = FormatConversationHistory(conversationHistory);
            var availableMcpToolsText = await GetAvailableMcpToolsDescriptionAsync(cancellationToken);
            var availableCapabilitiesText = GetAvailableCapabilitiesDescription();
            var currentTimeText = GetCurrentTimeInfo();

            // 프롬프트 변수 치환
            var prompt = promptTemplate
                .Replace("{SYSTEM_CONTEXT}", systemContext ?? "AI 에이전트")
                .Replace("{CURRENT_TIME}", currentTimeText)
                .Replace("{AVAILABLE_CAPABILITIES}", availableCapabilitiesText)
                .Replace("{AVAILABLE_MCP_TOOLS}", availableMcpToolsText)
                .Replace("{CONVERSATION_HISTORY}", historyText)
                .Replace("{USER_INPUT}", originalInput);

            Stopwatch stopwatch = Stopwatch.StartNew();

            // LLM 호출 (input-refinement 단계)
            var response = await _llmProvider.GenerateResponseAsync(prompt, cancellationToken);

            stopwatch.Stop();

            // LLM 요청/응답 로깅
            _ = Task.Run(() => _requestResponseLogger.LogLlmRequestResponseAsync(
                _llmProvider.GetLlmModel(), "InputRefinement", prompt, response, stopwatch.ElapsedMilliseconds, cancellationToken));

            _logger.LogDebug("Input refinement LLM response: {Response}", response);

            // JSON 응답 파싱
            var refinementResult = ParseRefinementResponse(response);

            if (refinementResult != null)
            {
                _logger.LogInformation("Input refined successfully with confidence: {Confidence}", refinementResult.IntentConfidence);
                return refinementResult;
            }
            else
            {
                _logger.LogWarning("Failed to parse LLM response, falling back to simple analysis");
                return CreateFallbackRefinement(originalInput);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refine input using LLM: {Input}", originalInput);
            return CreateFallbackRefinement(originalInput);
        }
    }

    private string FormatConversationHistory(IReadOnlyList<ConversationMessage> history)
    {
        if (history == null || history.Count == 0)
            return "이전 대화 없음";

        var historyText = string.Join("\n", history.Select(m => $"{m.Role}: {m.Content}"));
        return historyText;
    }

    private RefinedInput? ParseRefinementResponse(string response)
    {
        try
        {
            // JSON 코드 블록에서 JSON 추출
            var jsonStart = response.IndexOf('{');
            var jsonEnd = response.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonContent = response.Substring(jsonStart, jsonEnd - jsonStart + 1);

                var jsonDoc = JsonDocument.Parse(jsonContent);
                var root = jsonDoc.RootElement;

                var clarifiedIntent = root.GetProperty("clarified_intent").GetString() ?? "";
                var refinedQuery = root.GetProperty("refined_query").GetString() ?? "";

                var entities = new List<string>();
                if (root.TryGetProperty("extracted_entities", out var entitiesElement))
                {
                    foreach (var entity in entitiesElement.EnumerateArray())
                    {
                        if (entity.GetString() is string entityStr)
                            entities.Add(entityStr);
                    }
                }

                var context = new Dictionary<string, object>();
                if (root.TryGetProperty("context", out var contextElement))
                {
                    foreach (var prop in contextElement.EnumerateObject())
                    {
                        context[prop.Name] = prop.Value.ToString();
                    }
                }

                var suggestedPlan = root.TryGetProperty("suggested_plan", out var planElement)
                    ? planElement.GetString()
                    : null;

                var confidenceLevelStr = root.TryGetProperty("confidence_level", out var confidenceElement)
                    ? confidenceElement.GetString()
                    : "Medium";

                var confidenceLevel = Enum.TryParse<ConfidenceLevel>(confidenceLevelStr, out var parsedLevel)
                    ? parsedLevel
                    : ConfidenceLevel.Medium;

                return new RefinedInput(
                    refinedQuery,
                    clarifiedIntent,
                    refinedQuery,
                    entities,
                    context,
                    suggestedPlan,
                    confidenceLevel
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse refinement response JSON: {Response}", response);
        }

        return null;
    }

    private RefinedInput CreateFallbackRefinement(string originalInput)
    {
        return new RefinedInput(
            originalInput,
            "사용자 요청",
            originalInput,
            new List<string>(),
            new Dictionary<string, object>(),
            null,
            ConfidenceLevel.Medium
        );
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