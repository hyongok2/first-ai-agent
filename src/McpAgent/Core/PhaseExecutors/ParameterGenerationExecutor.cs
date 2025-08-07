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
    private readonly IDebugFileLogger _debugLogger;
    
    public int PhaseNumber => 3;
    
    public ParameterGenerationExecutor(
        ILogger<ParameterGenerationExecutor> logger,
        ILlmProvider llm,
        ISystemContextProvider contextProvider,
        IMcpClient mcpClient,
        IDebugFileLogger debugLogger)
    {
        _logger = logger;
        _llm = llm;
        _contextProvider = contextProvider;
        _mcpClient = mcpClient;
        _debugLogger = debugLogger;
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
            
            // Debug logging for prompt and response
            await _debugLogger.LogPromptAndResponseAsync(prompt, response, "parameter-generation");
            
            var parsed = ParseParameterResponse(response, selectedTool);
            
            _logger.LogInformation("Phase 3: Parameter generation completed. Confidence: {Confidence}, Tool calls: {ToolCallCount}, Missing info: {MissingCount}", 
                parsed.ParameterConfidence, parsed.ToolCalls.Count, parsed.MissingInfo.Count);
            
            var status = parsed.ParameterConfidence >= 0.8 ? ExecutionStatus.Success : ExecutionStatus.RequiresInput;
            _logger.LogInformation("Phase 3: Status determined as {Status}", status);
            
            return new PhaseResult
            {
                Phase = 3,
                Status = status,
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
        // 선택된 도구의 완전한 스키마 정보 제공 (Documentation, Examples 포함)
        var toolSchemaJson = JsonSerializer.Serialize(new
        {
            tool.Name,
            tool.Description,
            tool.Documentation,
            tool.Category,
            tool.Tags,
            Parameters = tool.Parameters?.Select(p => new
            {
                Name = p.Key,
                Type = p.Value.Type,
                Description = p.Value.Description,
                Required = p.Value.Required,
                DefaultValue = p.Value.DefaultValue
            }),
            Examples = tool.Examples?.Select(e => new {  // 모든 예시 포함 (파라미터 생성에 중요)
                e.Description,
                e.Arguments,
                e.ExpectedResult
            })
        }, new JsonSerializerOptions { WriteIndented = true });
        
        var functionData = JsonSerializer.Serialize(functionResult.Data, new JsonSerializerOptions { WriteIndented = true });
        
        return $@"
{systemContext}

**ROLE**: Expert Parameter Builder with Tool Mastery
**TASK**: Generate precise parameters using complete tool knowledge including documentation and examples

**USER INPUT**: {userInput}
**SELECTED FUNCTION**: {tool.Name}
**FUNCTION SELECTION CONTEXT**: {functionData}

**COMPLETE TOOL SPECIFICATION** (documentation, parameters, examples):
{toolSchemaJson}

**ADVANCED PARAMETER GENERATION PROCESS**:
1. **Study Examples**: Review ALL provided examples to understand usage patterns
2. **Apply Documentation**: Use tool documentation to understand parameter nuances  
3. **Parameter Mapping**: Map user input to parameters using examples as reference
4. **Validation**: Ensure parameter types match the schema exactly
5. **Context Integration**: Use system context to fill in missing details

**EXAMPLE-DRIVEN MAPPING**:
- Look for examples that closely match the user's request
- Copy parameter patterns from similar examples
- Adapt example values to fit the current user input
- Use example expected results to validate parameter correctness

**ENHANCED PARAMETER RULES**:
- **Exact Type Matching**: Ensure parameter types match schema (string, number, boolean, array, object)
- **Required Parameters**: All required parameters MUST be provided
- **Default Values**: Use schema default values when user input is unclear
- **Example Patterns**: Follow parameter patterns demonstrated in examples
- **Documentation Guidance**: Apply constraints and formats from documentation

**INTELLIGENT CONTEXT MAPPING**:
- File paths: Convert relative to absolute using working_directory
- Date/Time: Parse temporal expressions using current system time
- Location: Apply location context when available
- User preferences: Consider any user context from previous interactions

**MCP TOOL CALL JSON FORMAT** (CRITICAL - Follow exactly):

**REQUIRED JSON STRUCTURE**:
{{
    ""tool_calls"": [
        {{
            ""name"": ""exact_tool_name_from_schema"",
            ""parameters"": {{
                ""parameter_name"": ""parameter_value"",
                ""another_param"": 123,
                ""boolean_param"": true
            }},
            ""expected_output_type"": ""string""
        }}
    ],
    ""parameter_confidence"": 0.85,
    ""missing_info"": [""any missing required info""],
    ""reasoning"": ""detailed explanation with example references""
}}

**IMPORTANT**: The ""parameters"" object will be passed as arguments to the MCP tool via CallToolAsync(name, arguments). Use exact parameter names from the tool schema.

**JSON FORMAT RULES**:
- ALL keys must be in double quotes: ""name"", ""parameters""
- String values in double quotes: ""value""
- Numbers without quotes: 123, 0.85
- Booleans without quotes: true, false  
- Arrays with square brackets: [""item1"", ""item2""]
- Objects with curly braces: {{""key"": ""value""}}

**PARAMETER TYPE EXAMPLES**:
- String: ""file.txt"", ""/path/to/file"", ""Hello World""
- Number: 42, 3.14, 0
- Boolean: true, false
- Array: [""item1"", ""item2""], [1, 2, 3]
- Object: {{""key1"": ""value1"", ""key2"": 123}}

**COMPLETE REAL EXAMPLES**:

File reading:
{{
    ""tool_calls"": [{{
        ""name"": ""read_file"",
        ""parameters"": {{
            ""path"": ""document.txt"",
            ""encoding"": ""utf-8""
        }},
        ""expected_output_type"": ""string""
    }}],
    ""parameter_confidence"": 0.9,
    ""missing_info"": [],
    ""reasoning"": ""Using read_file example from tool documentation with path parameter""
}}

Directory listing:
{{
    ""tool_calls"": [{{
        ""name"": ""list_directory"",
        ""parameters"": {{
            ""path"": ""."",
            ""include_hidden"": false
        }},
        ""expected_output_type"": ""array""
    }}],
    ""parameter_confidence"": 0.95,
    ""missing_info"": [],
    ""reasoning"": ""Following list_directory example pattern for current directory""
}}

**CRITICAL REQUIREMENTS**:
- Copy tool name EXACTLY from the schema
- Match parameter names EXACTLY from the schema
- Use correct parameter types as specified in schema
- Reference specific examples from tool documentation in reasoning
- Set confidence based on parameter completeness and example matching

**YOUR TASK**: Generate the exact JSON format above for tool ""{tool.Name}""";
    }
    
    private ParameterGenerationResult ParseParameterResponse(string response, ToolDefinition tool)
    {
        try
        {
            _logger.LogDebug("Phase 3: Parsing parameter response for tool {ToolName}. Response length: {Length}", 
                tool.Name, response.Length);
                
            var cleanResponse = ExtractJsonFromResponse(response);
            _logger.LogDebug("Phase 3: Extracted JSON: {Json}", cleanResponse);
            
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
                    {
                        try
                        {
                            // JsonElement를 Dictionary로 변환
                            var paramsDict = new Dictionary<string, object>();
                            if (parameters.ValueKind == JsonValueKind.Object)
                            {
                                foreach (var prop in parameters.EnumerateObject())
                                {
                                    paramsDict[prop.Name] = prop.Value.ValueKind switch
                                    {
                                        JsonValueKind.String => prop.Value.GetString() ?? "",
                                        JsonValueKind.Number => prop.Value.GetDouble(),
                                        JsonValueKind.True => true,
                                        JsonValueKind.False => false,
                                        JsonValueKind.Null => null,
                                        _ => prop.Value.ToString()
                                    };
                                }
                            }
                            toolCall["parameters"] = paramsDict;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to parse parameters for tool call, using empty dictionary");
                            toolCall["parameters"] = new Dictionary<string, object>();
                        }
                    }
                    else
                    {
                        toolCall["parameters"] = new Dictionary<string, object>();
                    }
                    
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