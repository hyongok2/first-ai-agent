using System.Text.Json;
using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;
using McpAgent.Application.Interfaces;
using Microsoft.Extensions.Logging;
using McpAgent.Presentation.Console;

namespace McpAgent.Application.Services;

/// <summary>
/// 입력 분석과 기능 선택을 동시에 수행하는 통합 서비스
/// 즉시 응답 가능한 경우 응답 내용도 함께 생성하여 LLM 호출 횟수를 최적화
/// </summary>
public class IntegratedAnalysisService : BaseLlmService<IntegratedAnalysisService>, IIntegratedAnalysisService
{
    protected override PipelineType PipelineType => PipelineType.IntegratedAnalysis;

    public IntegratedAnalysisService(
        ILogger<IntegratedAnalysisService> logger,
        ILlmProviderFactory llmProviderFactory,
        IPromptService promptService,
        IRequestResponseLogger requestResponseLogger,
        IToolExecutor toolExecutor)
        : base(logger, llmProviderFactory, promptService, requestResponseLogger, toolExecutor)
    {
    }

    /// <summary>
    /// 입력 분석과 기능 선택을 동시에 수행
    /// 즉시 응답 가능한 경우 응답 내용도 함께 생성
    /// </summary>
    public async Task<IntegratedAnalysisResult> AnalyzeAndSelectAsync(
        string originalInput,
        IReadOnlyList<ConversationMessage> conversationHistory,
        string systemContext,
        IReadOnlyList<ToolExecution>? toolExecutionResults = null,
        List<string>? cumulativePlans = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Logger.LogInformation("Starting integrated analysis for input: {Input}", originalInput);

            // 프롬프트 변수 준비
            var replacements = new Dictionary<string, string>
            {
                {"{SYSTEM_CONTEXT}", systemContext ?? "AI 에이전트"},
                {"{CURRENT_TIME}", GetCurrentTimeInfo()},
                {"{AVAILABLE_CAPABILITIES}", GetAvailableCapabilitiesDescription()},
                {"{AVAILABLE_MCP_TOOLS}", await GetAvailableMcpToolsDescriptionAsync(cancellationToken)},
                {"{CONVERSATION_HISTORY}", FormatConversationHistory(conversationHistory)},
                {"{USER_INPUT}", originalInput},
                {"{TOOL_EXECUTION_RESULTS}", FormatToolExecutionResults(toolExecutionResults)},
                {"{CUMULATIVE_PLANS}", FormatCumulativePlans(cumulativePlans)}
            };

            // 통합 프롬프트 호출
            var prompt = await PreparePromptAsync("integrated-analysis", replacements, cancellationToken);
            var response = await CallLlmAsync(prompt, "IntegratedAnalysis", cancellationToken);

            // JSON 응답 파싱 (BaseLlmService에서 이미 빈 응답 체크 완료)
            var result = ParseIntegratedResponse(response, originalInput);
            
            Logger.LogInformation("Integrated analysis completed - Capability: {Capability}, HasDirectResponse: {HasResponse}", 
                result.SelectedCapability.Type, result.HasDirectResponse);

            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to perform integrated analysis for input: {Input}", originalInput);
            return CreateFallbackResult(originalInput);
        }
    }

    private IntegratedAnalysisResult ParseIntegratedResponse(string response, string originalInput)
    {
        try
        {
            var jsonContent = ExtractJsonFromResponse(response);
            if (string.IsNullOrEmpty(jsonContent))
            {
                Logger.LogWarning("No JSON content found in response, using fallback");
                return CreateFallbackResult(originalInput);
            }

            var jsonDoc = JsonDocument.Parse(jsonContent);
            var root = jsonDoc.RootElement;

            // 입력 분석 결과 파싱
            var refinedInput = ParseRefinedInput(root, originalInput);
            
            // 선택된 기능 파싱
            var selectedCapability = ParseSelectedCapability(root);
            
            // 직접 응답 여부와 내용 파싱
            var hasDirectResponse = root.TryGetProperty("has_direct_response", out var hasResponseElement) 
                && hasResponseElement.GetBoolean();
            
            var directResponseMessage = "";
            if (hasDirectResponse && root.TryGetProperty("direct_response_message", out var messageElement))
            {
                directResponseMessage = messageElement.GetString() ?? "";
            }

            return new IntegratedAnalysisResult(
                refinedInput,
                selectedCapability,
                hasDirectResponse,
                directResponseMessage
            );
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to parse integrated response JSON: {Response}", response);
            return CreateFallbackResult(originalInput);
        }
    }

    private RefinedInput ParseRefinedInput(JsonElement root, string originalInput)
    {
        try
        {
            if (!root.TryGetProperty("input_analysis", out var analysisElement))
            {
                return CreateFallbackRefinedInput(originalInput);
            }

            var clarifiedIntent = analysisElement.TryGetProperty("clarified_intent", out var intentElement)
                ? intentElement.GetString() ?? originalInput
                : originalInput;

            var refinedQuery = analysisElement.TryGetProperty("refined_query", out var queryElement)
                ? queryElement.GetString() ?? originalInput
                : originalInput;

            var entities = new List<string>();
            if (analysisElement.TryGetProperty("extracted_entities", out var entitiesElement))
            {
                foreach (var entity in entitiesElement.EnumerateArray())
                {
                    if (entity.GetString() is string entityStr)
                        entities.Add(entityStr);
                }
            }

            var context = new Dictionary<string, object>();
            if (analysisElement.TryGetProperty("context", out var contextElement))
            {
                foreach (var prop in contextElement.EnumerateObject())
                {
                    context[prop.Name] = prop.Value.ToString() ?? "";
                }
            }

            var suggestedPlan = analysisElement.TryGetProperty("suggested_plan", out var planElement)
                ? planElement.GetString()
                : null;

            var confidenceLevelStr = analysisElement.TryGetProperty("confidence_level", out var confidenceElement)
                ? confidenceElement.GetString() ?? "Medium"
                : "Medium";

            var confidenceLevel = Enum.TryParse<ConfidenceLevel>(confidenceLevelStr, out var parsedLevel)
                ? parsedLevel
                : ConfidenceLevel.Medium;

            return new RefinedInput(
                originalInput,
                clarifiedIntent,
                refinedQuery,
                entities,
                context,
                suggestedPlan,
                confidenceLevel
            );
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to parse refined input from integrated response");
            return CreateFallbackRefinedInput(originalInput);
        }
    }

    private SystemCapability ParseSelectedCapability(JsonElement root)
    {
        try
        {
            if (!root.TryGetProperty("capability_selection", out var capabilityElement))
            {
                return CreateFallbackCapability();
            }

            var capabilityTypeString = capabilityElement.TryGetProperty("selected_capability", out var typeElement)
                ? typeElement.GetString() ?? "SimpleChat"
                : "SimpleChat";

            if (!Enum.TryParse<SystemCapabilityType>(capabilityTypeString, out var capabilityType))
            {
                Logger.LogWarning("Unknown capability type: {Type}, defaulting to SimpleChat", capabilityTypeString);
                capabilityType = SystemCapabilityType.SimpleChat;
            }

            var description = capabilityElement.TryGetProperty("description", out var descriptionElement)
                ? descriptionElement.GetString() ?? ""
                : "";

            var reasoning = capabilityElement.TryGetProperty("reasoning", out var reasoningElement)
                ? reasoningElement.GetString() ?? ""
                : "";

            var parameters = new Dictionary<string, object>();
            if (capabilityElement.TryGetProperty("parameters", out var parametersElement))
            {
                foreach (var property in parametersElement.EnumerateObject())
                {
                    var value = ConvertJsonElementToObject(property.Value);
                    parameters[property.Name] = value;
                }
            }

            return new SystemCapability(capabilityType, description, reasoning, parameters);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to parse system capability from integrated response");
            return CreateFallbackCapability();
        }
    }

    private string FormatCumulativePlans(List<string>? cumulativePlans)
    {
        if (cumulativePlans == null || !cumulativePlans.Any())
        {
            return "진행 중인 계획이 없습니다.";
        }

        return string.Join("\n", cumulativePlans);
    }

    private IntegratedAnalysisResult CreateFallbackResult(string originalInput)
    {
        Logger.LogWarning("Creating fallback integrated analysis result for: {Input}", originalInput);

        var fallbackRefinedInput = CreateFallbackRefinedInput(originalInput);
        var fallbackCapability = CreateFallbackCapability();

        return new IntegratedAnalysisResult(
            fallbackRefinedInput,
            fallbackCapability,
            hasDirectResponse: false,
            directResponseMessage: ""
        );
    }

    private RefinedInput CreateFallbackRefinedInput(string originalInput)
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

    private SystemCapability CreateFallbackCapability()
    {
        return new SystemCapability(
            SystemCapabilityType.SimpleChat,
            "기본 대화 기능",
            "통합 분석 실패로 인한 기본 기능 선택",
            new Dictionary<string, object>()
        );
    }
}