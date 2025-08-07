using System.Text.Json;
using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;
using McpAgent.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace McpAgent.Application.Services;

public class ParameterGenerationService : IParameterGenerationService
{
    private readonly ILlmProvider _llmProvider;
    private readonly IPromptService _promptService;
    private readonly ILogger<ParameterGenerationService> _logger;
    private readonly IRequestResponseLogger _requestResponseLogger;
    private readonly IToolExecutor _toolExecutor;

    public ParameterGenerationService(
        ILlmProvider llmProvider,
        IPromptService promptService,
        ILogger<ParameterGenerationService> logger,
        IRequestResponseLogger requestResponseLogger,
        IToolExecutor toolExecutor)
    {
        _llmProvider = llmProvider;
        _promptService = promptService;
        _logger = logger;
        _requestResponseLogger = requestResponseLogger;
        _toolExecutor = toolExecutor;
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
            _logger.LogInformation("Generating parameters for tool: {Tool}", toolName);

            // Load the parameter generation prompt template
            var promptTemplate = await _promptService.GetPromptAsync("parameter-generation");
            
            // Format conversation history
            var conversationHistoryText = FormatConversationHistory(conversationHistory);
            var availableMcpToolsText = await GetAvailableMcpToolsDescriptionAsync(cancellationToken);
            var currentTimeText = GetCurrentTimeInfo();
            
            // Replace placeholders in the template
            var prompt = promptTemplate
                .Replace("{SYSTEM_CONTEXT}", systemContext)
                .Replace("{CURRENT_TIME}", currentTimeText)
                .Replace("{TOOL_NAME}", toolName)
                .Replace("{TOOL_DESCRIPTION}", toolDefinition.Description)
                .Replace("{TOOL_PARAMETERS}", FormatToolParameters(toolDefinition.Parameters))
                .Replace("{AVAILABLE_MCP_TOOLS}", availableMcpToolsText)
                .Replace("{CONVERSATION_HISTORY}", conversationHistoryText)
                .Replace("{CLARIFIED_INTENT}", refinedInput.ClarifiedIntent)
                .Replace("{REFINED_QUERY}", refinedInput.RefinedQuery)
                .Replace("{EXTRACTED_ENTITIES}", FormatExtractedEntities(refinedInput.ExtractedEntities));

            // Call LLM to generate parameters
            var response = await _llmProvider.GenerateResponseAsync(prompt, cancellationToken);
            
            // LLM 요청/응답 로깅
            _ = Task.Run(() => _requestResponseLogger.LogLlmRequestResponseAsync(
                "qwen3:32b", "ParameterGeneration", prompt, response, cancellationToken));
            
            // Parse the JSON response and extract parameters only
            var parameterResult = ParseParameterResult(response, toolName);
            
            _logger.LogInformation("Parameters generated successfully for tool: {Tool}", toolName);
            
            return parameterResult.Parameters;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate parameters for tool: {Tool}", toolName);
            
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
                _logger.LogError(ex, "Failed to generate parameters for tool: {Tool}", toolDef.Name);
                
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
            _logger.LogError(ex, "Failed to parse parameter result JSON: {Response}", response);
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

    private object ConvertJsonElementToObject(JsonElement element)
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

    private ToolParameterResult CreateFallbackParameterResult(string toolName, RefinedInput refinedInput)
    {
        _logger.LogWarning("Creating fallback parameter result for tool: {Tool}", toolName);
        
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

    public async Task<string> RecommendToolAsync(
        IReadOnlyList<ToolDefinition> availableTools,
        RefinedInput refinedInput,
        string systemContext,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Recommending tool from {Count} available tools", availableTools.Count);

            // Simple heuristic-based recommendation for now
            // In a more sophisticated implementation, this would use LLM
            var toolName = RecommendToolHeuristic(availableTools, refinedInput);
            
            _logger.LogInformation("Recommended tool: {Tool}", toolName);
            
            return toolName;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to recommend tool");
            
            // Return first available tool as fallback
            return availableTools.FirstOrDefault()?.Name ?? "echo";
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