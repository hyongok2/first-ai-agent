using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;
using McpAgent.Application.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using McpAgent.Presentation.Console;

namespace McpAgent.Application.Services;

/// <summary>
/// LLM 기반 InputRefinementService - input-refinement.txt 프롬프트 사용
/// </summary>
public class InputRefinementService : BaseLlmService<InputRefinementService>, IInputRefinementService
{
    public InputRefinementService(
        ILogger<InputRefinementService> logger,
        ILlmProvider llmProvider,
        IPromptService promptService,
        IRequestResponseLogger requestResponseLogger,
        IToolExecutor toolExecutor,
        ConsoleUIService consoleUIService)
        : base(logger, llmProvider, promptService, requestResponseLogger, toolExecutor, consoleUIService)
    {
    }

    public async Task<RefinedInput> RefineInputAsync(
        string originalInput,
        IReadOnlyList<ConversationMessage> conversationHistory,
        string systemContext,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Logger.LogInformation("Refining input using LLM with input-refinement prompt: {Input}", originalInput);
            ConsoleUIService.DisplayProcess("사용자의 의도를 파악 중입니다...");
            
            // 프롬프트 변수 준비
            var replacements = new Dictionary<string, string>
            {
                {"{SYSTEM_CONTEXT}", systemContext ?? "AI 에이전트"},
                {"{CURRENT_TIME}", GetCurrentTimeInfo()},
                {"{AVAILABLE_CAPABILITIES}", GetAvailableCapabilitiesDescription()},
                {"{AVAILABLE_MCP_TOOLS}", await GetAvailableMcpToolsDescriptionAsync(cancellationToken)},
                {"{CONVERSATION_HISTORY}", FormatConversationHistory(conversationHistory)},
                {"{USER_INPUT}", originalInput}
            };

            // 프롬프트 준비 및 LLM 호출
            var prompt = await PreparePromptAsync("input-refinement", replacements, cancellationToken);
            var response = await CallLlmAsync(prompt, "InputRefinement", cancellationToken);

            // JSON 응답 파싱
            var refinementResult = ParseRefinementResponse(response);

            if (refinementResult != null)
            {
                Logger.LogInformation("Input refined successfully with confidence: {Confidence}", refinementResult.IntentConfidence);
                return refinementResult;
            }
            else
            {
                Logger.LogWarning("Failed to parse LLM response, falling back to simple analysis");
                return CreateFallbackRefinement(originalInput);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to refine input using LLM: {Input}", originalInput);
            return CreateFallbackRefinement(originalInput);
        }
    }


    private RefinedInput? ParseRefinementResponse(string response)
    {
        try
        {
            // JSON 코드 블록에서 JSON 추출
            var jsonContent = ExtractJsonFromResponse(response);
            if (!string.IsNullOrEmpty(jsonContent))
            {

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
            Logger.LogError(ex, "Failed to parse refinement response JSON: {Response}", response);
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

}