using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;
using McpAgent.Application.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using McpAgent.Presentation.Console;

namespace McpAgent.Application.Services;

public class ResponseGenerationService : IResponseGenerationService
{
    private readonly ILlmProvider _llmProvider;
    private readonly IPromptService _promptService;
    private readonly ILogger<ResponseGenerationService> _logger;
    private readonly IRequestResponseLogger _requestResponseLogger;
    private readonly IToolExecutor _toolExecutor;
    private readonly ConsoleUIService _consoleUIService;

    public ResponseGenerationService(
        ILlmProvider llmProvider,
        IPromptService promptService,
        ILogger<ResponseGenerationService> logger,
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

    public async Task<string> GenerateResponseAsync(
        RefinedInput refinedInput,
        SystemCapability selectedCapability,
        IReadOnlyList<ConversationMessage> conversationHistory,
        IReadOnlyList<ToolExecution>? toolExecutionResults = null,
        string? systemContext = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Generating response using response-generation prompt for capability: {Capability}",
                selectedCapability.Type);
            _consoleUIService.DisplayProcess("사용자 응답을 생성 중입니다...");
            // response-generation.txt 프롬프트 사용
            return await GenerateResponseUsingTemplate(
                refinedInput,
                selectedCapability,
                systemContext,
                conversationHistory,
                toolExecutionResults,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate response for capability: {Capability}",
                selectedCapability.Type);

            // Return fallback response
            return CreateFallbackResponse(refinedInput, selectedCapability);
        }
    }

    private async Task<string> GenerateResponseUsingTemplate(
        RefinedInput refinedInput,
        SystemCapability selectedCapability,
        string? systemContext,
        IReadOnlyList<ConversationMessage> conversationHistory,
        IReadOnlyList<ToolExecution>? toolExecutionResults,
        CancellationToken cancellationToken)
    {
        // response-generation.txt 프롬프트 로드
        var promptTemplate = await _promptService.GetPromptAsync("response-generation", cancellationToken);

        // 대화 이력 포맷팅
        var historyText = FormatConversationHistory(conversationHistory);

        // 도구 실행 결과 포맷팅
        var toolResultsText = FormatToolExecutionResults(toolExecutionResults);
        var availableMcpToolsText = await GetAvailableMcpToolsDescriptionAsync(cancellationToken);
        var currentTimeText = GetCurrentTimeInfo();

        // 프롬프트 변수 치환
        var prompt = promptTemplate
            .Replace("{SYSTEM_CONTEXT}", systemContext ?? "AI 에이전트")
            .Replace("{CURRENT_TIME}", currentTimeText)
            .Replace("{AVAILABLE_MCP_TOOLS}", availableMcpToolsText)
            .Replace("{CONVERSATION_HISTORY}", historyText)
            .Replace("{ORIGINAL_INPUT}", refinedInput.OriginalInput)
            .Replace("{CLARIFIED_INTENT}", refinedInput.ClarifiedIntent)
            .Replace("{REFINED_QUERY}", refinedInput.RefinedQuery)
            .Replace("{CAPABILITY_TYPE}", selectedCapability.Type.ToString())
            .Replace("{CAPABILITY_REASONING}", selectedCapability.Reasoning)
            .Replace("{TOOL_EXECUTION_RESULTS}", toolResultsText);

        Stopwatch stopwatch = Stopwatch.StartNew();

        // LLM 호출 (response-generation 단계)
        var response = await _llmProvider.GenerateResponseAsync(prompt, cancellationToken);

        stopwatch.Stop();
        // LLM 요청/응답 로깅
        _ = Task.Run(() => _requestResponseLogger.LogLlmRequestResponseAsync(
            _llmProvider.GetLlmModel(), "ResponseGeneration", prompt, response, stopwatch.ElapsedMilliseconds, cancellationToken));

        _logger.LogDebug("Response generation LLM response: {Response}", response);

        return response;
    }

    private string FormatConversationHistory(IReadOnlyList<ConversationMessage> history)
    {
        if (history == null || history.Count == 0)
            return "이전 대화 없음";

        var historyText = string.Join("\n", history.Select(m => $"{m.Role}: {m.Content}"));
        return historyText;
    }

    private string FormatToolExecutionResults(IReadOnlyList<ToolExecution>? toolExecutionResults)
    {
        if (toolExecutionResults == null || toolExecutionResults.Count == 0)
            return "도구 실행 결과 없음";

        var results = toolExecutionResults
            .Select(result => $"도구: {result.ToolName}\n결과: {result.Result}\n상태: {result.IsSuccess}")
            .ToList();

        return string.Join("\n\n", results);
    }

    private string CreateFallbackResponse(RefinedInput refinedInput, SystemCapability selectedCapability)
    {
        return selectedCapability.Type switch
        {
            SystemCapabilityType.IntentClarification =>
                $"죄송하지만 '{refinedInput.OriginalInput}'에 대한 의도를 정확히 파악하지 못했습니다. 좀 더 구체적으로 설명해 주실 수 있나요?",

            SystemCapabilityType.SimpleChat =>
                "안녕하세요! 어떻게 도와드릴까요?",

            SystemCapabilityType.TaskCompletion =>
                "요청하신 작업을 처리했습니다. 추가로 도움이 필요하시면 말씀해 주세요.",

            SystemCapabilityType.McpTool =>
                "도구를 실행했지만 결과를 처리하는 중 문제가 발생했습니다. 다시 시도해 주세요.",

            SystemCapabilityType.TaskPlanning =>
                "작업 계획을 수립하는 중 문제가 발생했습니다. 요청을 다시 확인해 주세요.",

            SystemCapabilityType.ErrorHandling =>
                "오류가 발생했습니다. 문제를 해결하기 위해 도움이 필요하시면 말씀해 주세요.",

            _ => "죄송합니다. 응답을 생성하는 중 문제가 발생했습니다. 다시 시도해 주세요."
        };
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