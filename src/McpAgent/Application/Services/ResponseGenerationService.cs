using System.Diagnostics;
using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;
using McpAgent.Application.Interfaces;
using Microsoft.Extensions.Logging;
using McpAgent.Presentation.Console;

namespace McpAgent.Application.Services;

public class ResponseGenerationService : BaseLlmService<ResponseGenerationService>, IResponseGenerationService
{
    protected override PipelineType PipelineType => PipelineType.ResponseGeneration;

    public ResponseGenerationService(
        ILogger<ResponseGenerationService> logger,
        ILlmProviderFactory llmProviderFactory,
        IPromptService promptService,
        IRequestResponseLogger requestResponseLogger,
        IToolExecutor toolExecutor)
        : base(logger, llmProviderFactory, promptService, requestResponseLogger, toolExecutor)
    {
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
            Logger.LogInformation("Generating response using response-generation prompt for capability: {Capability}",
                selectedCapability.Type);
                
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
            Logger.LogError(ex, "Failed to generate response for capability: {Capability}",
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
        var promptTemplate = await PromptService.GetPromptAsync("response-generation", cancellationToken);

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

        var response = await CallLlmAsync(prompt, "ResponseGeneration", cancellationToken);

        Logger.LogDebug("Response generation LLM response: {Response}", response);

        return response;
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


}