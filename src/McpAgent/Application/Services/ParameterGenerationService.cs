using System.Text.Json;
using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;
using McpAgent.Application.Interfaces;
using Microsoft.Extensions.Logging;
using McpAgent.Presentation.Console;

namespace McpAgent.Application.Services;

public class ParameterGenerationService : BaseLlmService<ParameterGenerationService>, IParameterGenerationService
{
    protected override PipelineType PipelineType => PipelineType.ParameterGeneration;

    public ParameterGenerationService(
        ILogger<ParameterGenerationService> logger,
        ILlmProviderFactory llmProviderFactory,
        IPromptService promptService,
        IRequestResponseLogger requestResponseLogger,
        IToolExecutor toolExecutor)
        : base(logger, llmProviderFactory, promptService, requestResponseLogger, toolExecutor)
    {
    }

    public async Task<Dictionary<string, object>> GenerateParametersAsync(
        string toolName,
        ToolDefinition toolDefinition,
        RefinedInput refinedInput,
        IReadOnlyList<ConversationMessage> conversationHistory,
        string systemContext,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Logger.LogInformation("Generating parameters for tool: {Tool}", toolName);
            
            // 프롬프트 변수 준비
            var replacements = new Dictionary<string, string>
            {
                {"{SYSTEM_CONTEXT}", systemContext},
                {"{CURRENT_TIME}", GetCurrentTimeInfo()},
                {"{TOOL_NAME}", toolName},
                {"{TOOL_DESCRIPTION}", toolDefinition.Description},
                {"{TOOL_PARAMETERS}", FormatToolParameters(toolDefinition.Parameters)},
                {"{AVAILABLE_MCP_TOOLS}", await GetAvailableMcpToolsDescriptionAsync(cancellationToken)},
                {"{CONVERSATION_HISTORY}", FormatConversationHistory(conversationHistory)},
                {"{CLARIFIED_INTENT}", refinedInput.ClarifiedIntent},
                {"{REFINED_QUERY}", refinedInput.RefinedQuery},
                {"{SUGGESTED_PLAN}", refinedInput.SuggestedPlan ?? "특별한 계획이 제안되지 않았습니다."},
                {"{CUMULATIVE_PLANS}", ExtractCumulativePlansFromContext(systemContext)},
                {"{EXTRACTED_ENTITIES}", FormatExtractedEntities(refinedInput.ExtractedEntities)}
            };

            // 프롬프트 준비 및 LLM 호출
            var prompt = await PreparePromptAsync("parameter-generation", replacements, cancellationToken);
            var response = await CallLlmAsync(prompt, "ParameterGeneration", cancellationToken);

            // Parse the JSON response and extract parameters only
            var parameterResult = ParseParameterResult(response, toolName);

            Logger.LogInformation("Parameters generated successfully for tool: {Tool}", toolName);

            return parameterResult.Parameters;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to generate parameters for tool: {Tool}", toolName);

            // Return fallback parameter result
            var fallback = CreateFallbackParameterResult(toolName, refinedInput);
            return fallback.Parameters;
        }
    }

    public async Task<List<ToolParameterResult>> GenerateParametersForMultipleToolsAsync(
        RefinedInput refinedInput,
        List<ToolDefinition> toolDefinitions,
        string systemContext,
        string conversationHistory = "",
        CancellationToken cancellationToken = default)
    {
        var results = new List<ToolParameterResult>();

        foreach (var toolDef in toolDefinitions)
        {
            try
            {
                // Convert string history to conversation messages
                var conversationMessages = new List<ConversationMessage>();
                if (!string.IsNullOrEmpty(conversationHistory))
                {
                    conversationMessages.Add(new ConversationMessage(MessageRole.System, conversationHistory));
                }

                var parameters = await GenerateParametersAsync(
                    toolDef.Name,
                    toolDef,
                    refinedInput,
                    conversationMessages.AsReadOnly(),
                    systemContext,
                    cancellationToken);

                var result = new ToolParameterResult
                {
                    ToolName = toolDef.Name,
                    Parameters = parameters,
                    ValidationNotes = "",
                    MissingInfo = "",
                    IsValid = parameters.Any(),
                    GeneratedAt = DateTime.UtcNow
                };

                results.Add(result);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Failed to generate parameters for tool: {Tool}", toolDef.Name);

                // Add fallback result for this tool
                results.Add(CreateFallbackParameterResult(toolDef.Name, refinedInput));
            }
        }

        return results;
    }

    private ToolParameterResult ParseParameterResult(string response, string toolName)
    {
        try
        {
            // Extract JSON from response if it's wrapped in markdown
            var jsonResponse = ExtractJsonFromResponse(response);

            var jsonDocument = JsonDocument.Parse(jsonResponse);
            var root = jsonDocument.RootElement;

            return new ToolParameterResult
            {
                ToolName = toolName,
                Parameters = ParseParameters(root),
                ValidationNotes = root.TryGetProperty("validation_notes", out var notes)
                    ? notes.GetString() ?? "" : "",
                MissingInfo = root.TryGetProperty("missing_info", out var missing)
                    ? missing.GetString() ?? "" : "",
                IsValid = ValidateParameters(root),
                GeneratedAt = DateTime.UtcNow
            };
        }
        catch (JsonException ex)
        {
            Logger.LogError(ex, "Failed to parse parameter result JSON: {Response}", response);
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


    private bool ValidateParameters(JsonElement root)
    {
        // Check if there are any missing required parameters
        if (root.TryGetProperty("missing_info", out var missingInfo))
        {
            var missing = missingInfo.GetString();
            if (!string.IsNullOrEmpty(missing) && missing != "없음" && missing != "None")
            {
                return false;
            }
        }

        // Check if parameters object exists and is not empty
        if (root.TryGetProperty("parameters", out var parameters))
        {
            return parameters.ValueKind == JsonValueKind.Object &&
                   parameters.EnumerateObject().Any();
        }

        return false;
    }


    private string FormatExtractedEntities(List<string> entities)
    {
        if (!entities.Any())
        {
            return "추출된 엔티티가 없습니다.";
        }

        var formatted = entities.Select(entity => $"- {entity}");
        return string.Join('\n', formatted);
    }


    private ToolParameterResult CreateFallbackParameterResult(string toolName, RefinedInput refinedInput)
    {
        Logger.LogWarning("Creating fallback parameter result for tool: {Tool}", toolName);

        // Create simple parameters based on refined input
        var fallbackParameters = new Dictionary<string, object>();

        // Common parameter patterns based on tool name
        switch (toolName.ToLowerInvariant())
        {
            case "echo":
                fallbackParameters["text"] = refinedInput.RefinedQuery;
                break;

            case "search":
            case "web_search":
                fallbackParameters["query"] = refinedInput.RefinedQuery;
                break;

            case "file_read":
            case "read_file":
                if (refinedInput.Context.TryGetValue("file_path", out var filePath))
                {
                    fallbackParameters["path"] = filePath;
                }
                else
                {
                    fallbackParameters["path"] = "파일 경로를 지정해주세요";
                }
                break;

            case "file_write":
            case "write_file":
                if (refinedInput.Context.TryGetValue("file_path", out var writeFilePath))
                {
                    fallbackParameters["path"] = writeFilePath;
                }
                if (refinedInput.Context.TryGetValue("content", out var content))
                {
                    fallbackParameters["content"] = content;
                }
                break;

            default:
                // Generic fallback - use query or input as text parameter
                fallbackParameters["input"] = refinedInput.RefinedQuery;
                break;
        }

        return new ToolParameterResult
        {
            ToolName = toolName,
            Parameters = fallbackParameters,
            ValidationNotes = "자동 생성된 대안 파라미터",
            MissingInfo = "LLM 응답 실패로 인한 휴리스틱 기반 파라미터 생성",
            IsValid = false, // Mark as invalid since it's a fallback
            GeneratedAt = DateTime.UtcNow
        };
    }

    public Task<string> RecommendToolAsync(
        IReadOnlyList<ToolDefinition> availableTools,
        RefinedInput refinedInput,
        string systemContext,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Logger.LogInformation("Recommending tool from {Count} available tools", availableTools.Count);

            // Simple heuristic-based recommendation for now
            // In a more sophisticated implementation, this would use LLM
            var toolName = RecommendToolHeuristic(availableTools, refinedInput);

            Logger.LogInformation("Recommended tool: {Tool}", toolName);

            return Task.FromResult(toolName);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to recommend tool");

            // Return first available tool as fallback
            return Task.FromResult(availableTools.FirstOrDefault()?.Name ?? "echo");
        }
    }

    private string RecommendToolHeuristic(IReadOnlyList<ToolDefinition> availableTools, RefinedInput refinedInput)
    {
        var input = refinedInput.RefinedQuery.ToLowerInvariant();

        // Simple keyword-based heuristics
        if (input.Contains("시간") || input.Contains("time"))
        {
            var timeTool = availableTools.FirstOrDefault(t => t.Name.Contains("time"));
            if (timeTool != null) return timeTool.Name;
        }

        if (input.Contains("파일") || input.Contains("file"))
        {
            var fileTool = availableTools.FirstOrDefault(t => t.Name.Contains("file"));
            if (fileTool != null) return fileTool.Name;
        }

        if (input.Contains("웹") || input.Contains("검색") || input.Contains("search"))
        {
            var searchTool = availableTools.FirstOrDefault(t => t.Name.Contains("search"));
            if (searchTool != null) return searchTool.Name;
        }

        // Default to echo tool if available
        return availableTools.FirstOrDefault(t => t.Name == "echo")?.Name ??
               availableTools.FirstOrDefault()?.Name ?? "echo";
    }


    private string FormatToolParameters(Dictionary<string, ParameterDefinition> parameters)
    {
        if (parameters == null || parameters.Count == 0)
        {
            return "파라미터 정의가 없습니다.";
        }

        var paramDefs = parameters
            .Select(kvp => $"- {kvp.Key} ({kvp.Value.Type}): {kvp.Value.Description} {(kvp.Value.Required ? "[필수]" : "[선택]")}")
            .ToList();

        return string.Join('\n', paramDefs);
    }


}