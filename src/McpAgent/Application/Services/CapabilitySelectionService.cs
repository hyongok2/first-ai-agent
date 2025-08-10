using System.Text.Json;
using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;
using McpAgent.Application.Interfaces;
using Microsoft.Extensions.Logging;
using McpAgent.Presentation.Console;

namespace McpAgent.Application.Services;

public class CapabilitySelectionService : BaseLlmService<CapabilitySelectionService>, ICapabilitySelectionService
{
    protected override PipelineType PipelineType => PipelineType.CapabilitySelection;

    public CapabilitySelectionService(
        ILogger<CapabilitySelectionService> logger,
        ILlmProviderFactory llmProviderFactory,
        IPromptService promptService,
        IRequestResponseLogger requestResponseLogger,
        IToolExecutor toolExecutor)
        : base(logger, llmProviderFactory, promptService, requestResponseLogger, toolExecutor)
    {
    }

    public async Task<SystemCapability> SelectCapabilityAsync(
        RefinedInput refinedInput,
        IReadOnlyList<ConversationMessage> conversationHistory,
        string systemContext,
        IReadOnlyList<ToolExecution>? toolExecutionResults = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Logger.LogInformation("Selecting capability for intent: {Intent}", refinedInput.ClarifiedIntent);

            // 프롬프트 변수 준비
            var replacements = new Dictionary<string, string>
            {
                {"{SYSTEM_CONTEXT}", systemContext},
                {"{CURRENT_TIME}", GetCurrentTimeInfo()},
                {"{AVAILABLE_CAPABILITIES}", GetAvailableCapabilitiesDescription()},
                {"{AVAILABLE_MCP_TOOLS}", await GetAvailableMcpToolsDescriptionAsync(cancellationToken)},
                {"{CONVERSATION_HISTORY}", FormatConversationHistory(conversationHistory)},
                {"{CLARIFIED_INTENT}", refinedInput.ClarifiedIntent},
                {"{REFINED_QUERY}", refinedInput.RefinedQuery},
                {"{SUGGESTED_PLAN}", refinedInput.SuggestedPlan ?? "특별한 계획이 제안되지 않았습니다."},
                {"{CUMULATIVE_PLANS}", ExtractCumulativePlansFromContext(systemContext)},
                {"{CONFIDENCE_LEVEL}", refinedInput.IntentConfidence.ToString()},
                {"{TOOL_EXECUTION_RESULTS}", FormatToolExecutionResults(toolExecutionResults)}
            };

            // 프롬프트 준비 및 LLM 호출
            var prompt = await PreparePromptAsync("capability-selection", replacements, cancellationToken);

            // Call LLM to select capability using base class method
            var response = await CallLlmAsync(prompt, "CapabilitySelection", cancellationToken);
            
            // Parse the JSON response
            var selectedCapability = ParseCapabilitySelection(response);
            
            Logger.LogInformation("Selected capability: {Capability}", selectedCapability.Type);
            
            return selectedCapability;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to select capability for intent: {Intent}", refinedInput.ClarifiedIntent);
            
            // Return fallback capability selection
            return CreateFallbackCapabilitySelection(refinedInput);
        }
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
                Logger.LogWarning("Unknown capability type: {Type}, defaulting to SimpleChat", capabilityTypeString);
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
            Logger.LogError(ex, "Failed to parse capability selection JSON: {Response}", response);
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
                var value = ConvertJsonElementToObject(property.Value);
                parameters[property.Name] = value;
            }
        }
        
        return parameters;
    }


    private SystemCapability CreateFallbackCapabilitySelection(RefinedInput refinedInput)
    {
        Logger.LogWarning("Creating fallback capability selection for: {Intent}", refinedInput.ClarifiedIntent);
        
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



}