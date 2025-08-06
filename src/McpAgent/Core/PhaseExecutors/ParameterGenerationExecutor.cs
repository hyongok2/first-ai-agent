using System.Text.Json;
using McpAgent.Mcp;
using McpAgent.Models;
using McpAgent.Providers;
using McpAgent.Services;
using Microsoft.Extensions.Logging;

namespace McpAgent.Core.PhaseExecutors;

public class ParameterGenerationExecutor : IPhaseExecutor
{
    private readonly ILogger<ParameterGenerationExecutor> _logger;
    private readonly ILlmProvider _llm;
    private readonly ISystemContextProvider _contextProvider;
    private readonly IMcpClient _mcpClient;
    
    public int PhaseNumber => 3;
    
    public ParameterGenerationExecutor(
        ILogger<ParameterGenerationExecutor> logger,
        ILlmProvider llm,
        ISystemContextProvider contextProvider,
        IMcpClient mcpClient)
    {
        _logger = logger;
        _llm = llm;
        _contextProvider = contextProvider;
        _mcpClient = mcpClient;
    }
    
    public async Task<PhaseResult> ExecuteAsync(ConversationState state, string userInput, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!state.PhaseHistory.TryGetValue(2, out var functionResult))
            {
                return CreateErrorResult("Function selection result not found");
            }
            
            var primaryFunction = functionResult.Data.GetValueOrDefault("primary_function")?.ToString();
            if (string.IsNullOrEmpty(primaryFunction))
            {
                return CreateErrorResult("No primary function selected");
            }
            
            // Chat 응답의 경우 별도 처리
            if (primaryFunction == "chat_response")
            {
                return new PhaseResult
                {
                    Phase = 3,
                    Status = ExecutionStatus.Success,
                    Data = new Dictionary<string, object>
                    {
                        ["tool_calls"] = new List<object>
                        {
                            new
                            {
                                name = "chat_response",
                                parameters = new { message = userInput },
                                expected_output_type = "text"
                            }
                        },
                        ["parameter_confidence"] = 1.0
                    },
                    ConfidenceScore = 1.0
                };
            }
            
            var availableTools = await _mcpClient.GetAvailableToolsAsync(cancellationToken);
            var selectedTool = availableTools.FirstOrDefault(t => t.Name == primaryFunction);
            
            if (selectedTool == null)
            {
                return CreateErrorResult($"Tool '{primaryFunction}' not found");
            }
            
            var systemContext = await _contextProvider.FormatContextForPromptAsync(ContextLevel.Standard);
            var prompt = BuildParameterGenerationPrompt(systemContext, userInput, selectedTool, functionResult);
            
            _logger.LogDebug("Phase 3: Generating parameters for function {FunctionName}", primaryFunction);
            var response = await _llm.GenerateResponseAsync(prompt, [], cancellationToken);
            
            var parsed = ParseParameterResponse(response, selectedTool);
            
            return new PhaseResult
            {
                Phase = 3,
                Status = parsed.ParameterConfidence >= 0.8 ? ExecutionStatus.Success : ExecutionStatus.RequiresInput,
                Data = parsed.ToDictionary(),
                ConfidenceScore = parsed.ParameterConfidence,
                RequiresUserInput = parsed.MissingInfo.Any(),
                Messages = parsed.MissingInfo.Select(info => $"Missing information: {info}").ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in parameter generation phase");
            return CreateErrorResult(ex.Message);
        }
    }
    
    private string BuildParameterGenerationPrompt(string systemContext, string userInput, ToolDefinition tool, PhaseResult functionResult)
    {
        var toolSchemaJson = JsonSerializer.Serialize(new
        {
            tool.Name,
            tool.Description,
            Parameters = tool.Parameters?.Select(p => new
            {
                Name = p.Key,
                Type = p.Value.Type,
                Description = p.Value.Description,
                Required = p.Value.Required,
                DefaultValue = p.Value.DefaultValue
            })
        }, new JsonSerializerOptions { WriteIndented = true });
        
        var functionData = JsonSerializer.Serialize(functionResult.Data, new JsonSerializerOptions { WriteIndented = true });
        
        return $@"
{systemContext}

**ROLE**: Parameter Builder
**TASK**: Generate precise parameters for the selected function

**USER INPUT**: {userInput}
**SELECTED FUNCTION**: {tool.Name}
**FUNCTION SELECTION CONTEXT**: {functionData}
**TOOL SCHEMA**: {toolSchemaJson}

**PARAMETER GENERATION RULES**:
1. Extract all required parameters from user input and system context
2. Use system context for default values (current directory, time, etc.)
3. Validate parameter types and formats
4. Identify any missing required information
5. Provide reasonable defaults where possible

**CONTEXT MAPPING**:
- File paths: Use working_directory from system context if relative path
- Time/Date: Use current_time from system context for ""now"", ""today"", etc.
- Location: Use location_info if available for location-based queries

**RESPONSE FORMAT** (JSON only):
{{
    ""tool_calls"": [
        {{
            ""name"": ""{tool.Name}"",
            ""parameters"": {{
                ""param1"": ""value1"",
                ""param2"": ""value2""
            }},
            ""expected_output_type"": ""string|object|array""
        }}
    ],
    ""parameter_confidence"": 0.0,
    ""missing_info"": [],
    ""reasoning"": ""parameter generation explanation""
}}";
    }
    
    private ParameterGenerationResult ParseParameterResponse(string response, ToolDefinition tool)
    {
        try
        {
            var cleanResponse = ExtractJsonFromResponse(response);
            var parsed = JsonSerializer.Deserialize<JsonElement>(cleanResponse);
            
            var result = new ParameterGenerationResult
            {
                ParameterConfidence = parsed.TryGetProperty("parameter_confidence", out var confidence) ? 
                    confidence.GetDouble() : 0.0,
                Reasoning = parsed.TryGetProperty("reasoning", out var reasoning) ? 
                    reasoning.GetString() ?? "" : ""
            };
            
            // Tool calls 파싱
            if (parsed.TryGetProperty("tool_calls", out var toolCalls))
            {
                result.ToolCalls = new List<object>();
                foreach (var call in toolCalls.EnumerateArray())
                {
                    var toolCall = new Dictionary<string, object>();
                    
                    if (call.TryGetProperty("name", out var name))
                        toolCall["name"] = name.GetString() ?? "";
                    if (call.TryGetProperty("expected_output_type", out var outputType))
                        toolCall["expected_output_type"] = outputType.GetString() ?? "string";
                    if (call.TryGetProperty("parameters", out var parameters))
                        toolCall["parameters"] = JsonSerializer.Deserialize<Dictionary<string, object>>(parameters.GetRawText()) ?? new();
                    
                    result.ToolCalls.Add(toolCall);
                }
            }
            
            // Missing info 파싱
            if (parsed.TryGetProperty("missing_info", out var missingInfo))
            {
                result.MissingInfo = missingInfo.EnumerateArray()
                    .Select(info => info.GetString() ?? "")
                    .Where(info => !string.IsNullOrEmpty(info))
                    .ToList();
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse parameter response, using heuristic generation");
            return GenerateParametersHeuristically(response, tool);
        }
    }
    
    private string ExtractJsonFromResponse(string response)
    {
        var startIndex = response.IndexOf('{');
        var endIndex = response.LastIndexOf('}');
        
        if (startIndex >= 0 && endIndex > startIndex)
        {
            return response.Substring(startIndex, endIndex - startIndex + 1);
        }
        
        return response;
    }
    
    private ParameterGenerationResult GenerateParametersHeuristically(string response, ToolDefinition tool)
    {
        var result = new ParameterGenerationResult
        {
            ParameterConfidence = 0.5,
            Reasoning = "Heuristic parameter generation",
            ToolCalls = new List<object>()
        };
        
        // 기본 파라미터 생성 로직
        var parameters = new Dictionary<string, object>();
        
        // 도구별 휴리스틱 파라미터 생성
        switch (tool.Name)
        {
            case "list_directory":
                parameters["path"] = ".";
                break;
            case "read_file":
                // 응답에서 파일명 추출 시도
                var words = response.Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                var possibleFile = words.FirstOrDefault(w => w.Contains('.') && w.Length > 3);
                parameters["path"] = possibleFile ?? "README.md";
                break;
            case "write_file":
                parameters["path"] = "output.txt";
                parameters["content"] = "Generated content";
                break;
            default:
                result.MissingInfo.Add($"Unknown tool parameters for {tool.Name}");
                result.ParameterConfidence = 0.2;
                break;
        }
        
        result.ToolCalls.Add(new Dictionary<string, object>
        {
            ["name"] = tool.Name,
            ["parameters"] = parameters,
            ["expected_output_type"] = "string"
        });
        
        return result;
    }
    
    private PhaseResult CreateErrorResult(string message)
    {
        return new PhaseResult
        {
            Phase = 3,
            Status = ExecutionStatus.Failure,
            ErrorMessage = message,
            Data = new Dictionary<string, object> { ["missing_parameters"] = true }
        };
    }
}

public class ParameterGenerationResult
{
    public List<object> ToolCalls { get; set; } = new();
    public double ParameterConfidence { get; set; }
    public List<string> MissingInfo { get; set; } = new();
    public string Reasoning { get; set; } = string.Empty;
    
    public Dictionary<string, object> ToDictionary()
    {
        return new Dictionary<string, object>
        {
            ["tool_calls"] = ToolCalls,
            ["parameter_confidence"] = ParameterConfidence,
            ["missing_info"] = MissingInfo,
            ["reasoning"] = Reasoning,
            ["missing_parameters"] = MissingInfo.Any()
        };
    }
}