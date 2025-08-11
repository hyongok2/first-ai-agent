using System.Diagnostics;
using System.Text.Json;
using McpAgent.Application.Interfaces;
using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace McpAgent.Application.Services;

/// <summary>
/// LLM 기반 서비스들의 공통 기능을 제공하는 추상 Base 클래스
/// </summary>
public abstract class BaseLlmService<TService>
{
    protected readonly ILogger<TService> Logger;
    protected readonly ILlmProviderFactory LlmProviderFactory;
    protected readonly IPromptService PromptService;
    protected readonly IRequestResponseLogger RequestResponseLogger;
    protected readonly IToolExecutor ToolExecutor;

    /// <summary>
    /// 이 서비스가 사용하는 파이프라인 타입
    /// </summary>
    protected abstract PipelineType PipelineType { get; }

    protected BaseLlmService(
        ILogger<TService> logger,
        ILlmProviderFactory llmProviderFactory,
        IPromptService promptService,
        IRequestResponseLogger requestResponseLogger,
        IToolExecutor toolExecutor)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        LlmProviderFactory = llmProviderFactory ?? throw new ArgumentNullException(nameof(llmProviderFactory));
        PromptService = promptService ?? throw new ArgumentNullException(nameof(promptService));
        RequestResponseLogger = requestResponseLogger ?? throw new ArgumentNullException(nameof(requestResponseLogger));
        ToolExecutor = toolExecutor ?? throw new ArgumentNullException(nameof(toolExecutor));
    }

    /// <summary>
    /// LLM을 호출하고 응답을 받아오는 공통 메서드
    /// </summary>
    protected async Task<string> CallLlmAsync(
        string prompt,
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        // 파이프라인 타입에 맞는 LLM Provider 가져오기
        var llmProvider = LlmProviderFactory.CreateForPipeline(PipelineType);
        
        var stopwatch = Stopwatch.StartNew();
        
        var response = await llmProvider.GenerateResponseAsync(prompt, cancellationToken);
        
        stopwatch.Stop();

        // 빈 응답 체크
        if (string.IsNullOrWhiteSpace(response))
        {
            Logger.LogError("{ServiceName} received empty response from LLM (Pipeline: {PipelineType}, Model: {Model})", 
                serviceName, PipelineType, llmProvider.GetLlmModel());
            
            // 빈 응답을 로깅하고 예외 발생
            _ = Task.Run(() => RequestResponseLogger.LogLlmRequestResponseAsync(
                llmProvider.GetLlmModel(), 
                serviceName, 
                prompt, 
                "[EMPTY RESPONSE]", 
                stopwatch.ElapsedMilliseconds, 
                cancellationToken));
            
            throw new InvalidOperationException($"LLM returned empty response for {serviceName} (Model: {llmProvider.GetLlmModel()})");
        }

        // 비동기로 요청/응답 로깅
        _ = Task.Run(() => RequestResponseLogger.LogLlmRequestResponseAsync(
            llmProvider.GetLlmModel(), 
            serviceName, 
            prompt, 
            response, 
            stopwatch.ElapsedMilliseconds, 
            cancellationToken));

        Logger.LogDebug("{ServiceName} LLM response: {Response} (Pipeline: {PipelineType}, Model: {Model})", 
            serviceName, response, PipelineType, llmProvider.GetLlmModel());
        
        return response;
    }

    /// <summary>
    /// 대화 이력을 포맷팅하는 공통 메서드
    /// </summary>
    protected string FormatConversationHistory(IReadOnlyList<ConversationMessage> conversationHistory)
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

    /// <summary>
    /// 현재 시간 정보를 생성하는 공통 메서드
    /// </summary>
    protected string GetCurrentTimeInfo()
    {
        var now = DateTime.Now;
        var utcNow = DateTime.UtcNow;

        return $@"현재 시간: {now:yyyy-MM-dd HH:mm:ss} (현지 시간)
UTC 시간: {utcNow:yyyy-MM-dd HH:mm:ss}
요일: {GetKoreanDayOfWeek(now.DayOfWeek)}
타임존: {TimeZoneInfo.Local.DisplayName}";
    }

    /// <summary>
    /// 요일을 한국어로 변환
    /// </summary>
    private static string GetKoreanDayOfWeek(DayOfWeek dayOfWeek) => dayOfWeek switch
    {
        DayOfWeek.Monday => "월요일",
        DayOfWeek.Tuesday => "화요일",
        DayOfWeek.Wednesday => "수요일",
        DayOfWeek.Thursday => "목요일",
        DayOfWeek.Friday => "금요일",
        DayOfWeek.Saturday => "토요일",
        DayOfWeek.Sunday => "일요일",
        _ => dayOfWeek.ToString()
    };

    /// <summary>
    /// 사용 가능한 MCP 도구 설명을 가져오는 공통 메서드
    /// </summary>
    protected async Task<string> GetAvailableMcpToolsDescriptionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var availableTools = await ToolExecutor.GetAvailableToolsAsync(cancellationToken);

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
            Logger.LogWarning(ex, "Failed to get available MCP tools information");
            return "MCP 도구 정보를 가져올 수 없습니다.";
        }
    }

    /// <summary>
    /// 시스템 컨텍스트에서 누적 계획을 추출하는 공통 메서드
    /// </summary>
    protected static string ExtractCumulativePlansFromContext(string systemContext)
    {
        // systemContext에서 [진행 계획 상태] 섹션 추출
        var startMarker = "[진행 계획 상태]";
        var startIndex = systemContext.IndexOf(startMarker);

        if (startIndex == -1)
            return "아직 제안된 계획이 없습니다.";

        var plansSection = systemContext.Substring(startIndex + startMarker.Length).Trim();
        return string.IsNullOrEmpty(plansSection) ? "아직 제안된 계획이 없습니다." : plansSection;
    }

    /// <summary>
    /// 사용 가능한 능력 설명을 가져오는 공통 메서드
    /// </summary>
    protected string GetAvailableCapabilitiesDescription()
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

    /// <summary>
    /// JSON 응답에서 JSON 부분만 추출하는 공통 메서드
    /// </summary>
    protected string ExtractJsonFromResponse(string response)
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

        // 마크다운 블록이 없으면 중괄호 기준으로 추출
        var jsonStart = response.IndexOf('{');
        var jsonEnd = response.LastIndexOf('}');

        if (jsonStart >= 0 && jsonEnd > jsonStart)
        {
            return response.Substring(jsonStart, jsonEnd - jsonStart + 1);
        }

        // If no markdown blocks or braces found, assume entire response is JSON
        return response.Trim();
    }

    /// <summary>
    /// JsonElement를 object로 변환하는 헬퍼 메서드
    /// </summary>
    protected object ConvertJsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.TryGetInt32(out var intValue) ? intValue : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => element.EnumerateArray()
                .Select(ConvertJsonElementToObject)
                .ToArray(),
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => ConvertJsonElementToObject(p.Value)),
            JsonValueKind.Null => null!,
            _ => element.GetRawText()
        };
    }

    /// <summary>
    /// 도구 실행 결과를 포맷팅하는 공통 메서드
    /// </summary>
    protected string FormatToolExecutionResults(IReadOnlyList<ToolExecution>? toolExecutionResults)
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

    /// <summary>
    /// 프롬프트 템플릿을 로드하고 기본 변수들을 치환하는 공통 메서드
    /// </summary>
    protected async Task<string> PreparePromptAsync(
        string promptName,
        Dictionary<string, string> replacements,
        CancellationToken cancellationToken = default)
    {
        var promptTemplate = await PromptService.GetPromptAsync(promptName, cancellationToken);

        foreach (var replacement in replacements)
        {
            promptTemplate = promptTemplate.Replace(replacement.Key, replacement.Value);
        }

        return promptTemplate;
    }
}