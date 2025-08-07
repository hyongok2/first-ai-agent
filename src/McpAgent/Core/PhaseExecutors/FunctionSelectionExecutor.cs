using System.Text.Json;
using McpAgent.Mcp;
using McpAgent.Models;
using McpAgent.Providers;
using McpAgent.Services;
using Microsoft.Extensions.Logging;

namespace McpAgent.Core.PhaseExecutors;

public class FunctionSelectionExecutor : IPhaseExecutor
{
    private readonly ILogger<FunctionSelectionExecutor> _logger;
    private readonly ILlmProvider _llm;
    private readonly ISystemContextProvider _contextProvider;
    private readonly IMcpClient _mcpClient;
    private readonly IDebugFileLogger _debugLogger;
    
    public int PhaseNumber => 2;
    
    public FunctionSelectionExecutor(
        ILogger<FunctionSelectionExecutor> logger,
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
            if (!state.PhaseHistory.TryGetValue(1, out var intentResult))
            {
                return CreateErrorResult("Intent analysis result not found");
            }
            
            var systemContext = await _contextProvider.FormatContextForPromptAsync(ContextLevel.Minimal);
            var availableFunctions = await _mcpClient.GetAvailableToolsAsync(cancellationToken);
            
            var prompt = BuildFunctionSelectionPrompt(systemContext, intentResult, availableFunctions);
            
            _logger.LogDebug("Phase 2: Selecting appropriate function");
            var response = await _llm.GenerateResponseAsync(prompt, [], cancellationToken);
            
            // Debug logging for prompt and response
            await _debugLogger.LogPromptAndResponseAsync(prompt, response, "function-selection");
            
            var parsed = ParseFunctionResponse(response, availableFunctions);
            
            return new PhaseResult
            {
                Phase = 2,
                Status = parsed.IsValid ? ExecutionStatus.Success : ExecutionStatus.NeedsRetry,
                Data = parsed.ToDictionary(),
                ConfidenceScore = parsed.ConfidenceScore,
                RequiresUserInput = !parsed.IsValid && parsed.ConfidenceScore < 0.5
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in function selection phase");
            return CreateErrorResult(ex.Message);
        }
    }
    
    private string BuildFunctionSelectionPrompt(string systemContext, PhaseResult intentResult, List<ToolDefinition> functions)
    {
        // 완전한 도구 정보 제공 (Documentation, Examples 포함)
        var functionsJson = JsonSerializer.Serialize(functions.Select(f => new {
            f.Name, 
            f.Description,
            f.Documentation,
            f.Category,
            f.Tags,
            Parameters = f.Parameters?.Select(p => new { 
                p.Key, 
                p.Value.Type, 
                p.Value.Description, 
                p.Value.Required,
                p.Value.DefaultValue
            }),
            Examples = f.Examples?.Take(2).Select(e => new {  // 최대 2개 예시만 표시 (토큰 절약)
                e.Description,
                e.Arguments,
                e.ExpectedResult
            })
        }), new JsonSerializerOptions { WriteIndented = true });
        
        var intentData = JsonSerializer.Serialize(intentResult.Data, new JsonSerializerOptions { WriteIndented = true });
        
        return $@"
{systemContext}

**ROLE**: Expert Function Selector with Complete Tool Knowledge
**TASK**: Analyze complete tool capabilities and select the most appropriate function

**USER INTENT ANALYSIS**: {intentData}

**COMPLETE FUNCTION CATALOG** (with documentation, parameters, and examples):
{functionsJson}

**INTELLIGENT SELECTION PROCESS**:
1. **Capability Analysis**: Review each function's documentation and examples
2. **Parameter Compatibility**: Ensure required parameters can be derived from context
3. **Example Matching**: Look for similar use cases in the provided examples
4. **Context Requirements**: Verify system context supports the function

**ADVANCED SELECTION CRITERIA**:
- **Exact Match**: Function examples directly match user intent
- **Parameter Availability**: All required parameters can be determined
- **Documentation Clarity**: Function documentation clearly supports the use case
- **Category Relevance**: Function category aligns with intent type
- **Context Sufficiency**: System context provides necessary information

**ENHANCED DECISION LOGIC**:
- Review function examples to find closest match to user request
- Check if required parameters can be extracted from context or user input
- Consider function documentation to understand full capabilities
- Use category and tags for additional context about function purpose

**FUNCTION SELECTION JSON FORMAT** (CRITICAL - Follow exactly):

**REQUIRED JSON STRUCTURE**:
{{
    ""primary_function"": ""exact_function_name_from_catalog"",
    ""secondary_functions"": [""function2"", ""function3""],
    ""execution_strategy"": ""single"",
    ""context_requirements"": {{
        ""time_context"": true,
        ""location_context"": false,
        ""system_context"": true
    }},
    ""required_parameters"": {{
        ""param_name"": ""expected_value_type""
    }},
    ""confidence_score"": 0.85,
    ""reasoning"": ""detailed explanation referencing specific documentation and examples""
}}

**JSON FORMAT RULES**:
- ALL keys in double quotes: ""primary_function""
- String values in double quotes: ""function_name""
- Numbers without quotes: 0.85
- Booleans without quotes: true, false
- Arrays with square brackets: [""func1"", ""func2""]
- Empty arrays: []
- Empty objects: {{}}

**EXECUTION STRATEGY VALUES**:
- ""single"": One function call only
- ""sequential"": Multiple functions in order
- ""parallel"": Multiple functions simultaneously  

**COMPLETE REAL EXAMPLES**:

File operation selection:
{{
    ""primary_function"": ""read_file"",
    ""secondary_functions"": [],
    ""execution_strategy"": ""single"",
    ""context_requirements"": {{
        ""time_context"": false,
        ""location_context"": false,
        ""system_context"": true
    }},
    ""required_parameters"": {{
        ""path"": ""string""
    }},
    ""confidence_score"": 0.9,
    ""reasoning"": ""User wants to read a file. read_file function documentation shows exact match with path parameter required.""
}}

Multi-step operation:
{{
    ""primary_function"": ""list_directory"",
    ""secondary_functions"": [""read_file""],
    ""execution_strategy"": ""sequential"",
    ""context_requirements"": {{
        ""time_context"": false,
        ""location_context"": false,
        ""system_context"": true
    }},
    ""required_parameters"": {{
        ""path"": ""string""
    }},
    ""confidence_score"": 0.85,
    ""reasoning"": ""User wants to explore directory contents then read specific files. Examples show list_directory followed by read_file pattern.""
}}

**CRITICAL REQUIREMENTS**:
- Copy function names EXACTLY from the catalog
- Reference specific examples from function documentation
- Set confidence based on documentation match and example similarity
- Explain reasoning with specific references to function capabilities";
    }
    
    private FunctionSelectionResult ParseFunctionResponse(string response, List<ToolDefinition> availableFunctions)
    {
        try
        {
            var cleanResponse = ExtractJsonFromResponse(response);
            var parsed = JsonSerializer.Deserialize<JsonElement>(cleanResponse);
            
            var result = new FunctionSelectionResult
            {
                PrimaryFunction = parsed.TryGetProperty("primary_function", out var primaryFunc) ? 
                    primaryFunc.GetString() ?? "" : "",
                ExecutionStrategy = parsed.TryGetProperty("execution_strategy", out var strategy) ? 
                    strategy.GetString() ?? "single" : "single",
                ConfidenceScore = parsed.TryGetProperty("confidence_score", out var confidence) ? 
                    confidence.GetDouble() : 0.0,
                Reasoning = parsed.TryGetProperty("reasoning", out var reasoning) ? 
                    reasoning.GetString() ?? "" : ""
            };
            
            // Secondary functions 파싱
            if (parsed.TryGetProperty("secondary_functions", out var secondaryFuncs))
            {
                result.SecondaryFunctions = secondaryFuncs.EnumerateArray()
                    .Select(f => f.GetString() ?? "")
                    .Where(f => !string.IsNullOrEmpty(f))
                    .ToList();
            }
            
            // Context requirements 파싱
            if (parsed.TryGetProperty("context_requirements", out var contextReqs))
            {
                result.ContextRequirements = new Dictionary<string, bool>();
                if (contextReqs.TryGetProperty("time_context", out var timeCtx))
                    result.ContextRequirements["time_context"] = timeCtx.GetBoolean();
                if (contextReqs.TryGetProperty("location_context", out var locCtx))
                    result.ContextRequirements["location_context"] = locCtx.GetBoolean();
                if (contextReqs.TryGetProperty("system_context", out var sysCtx))
                    result.ContextRequirements["system_context"] = sysCtx.GetBoolean();
            }
            
            // Required parameters 파싱
            if (parsed.TryGetProperty("required_parameters", out var reqParams))
            {
                result.RequiredParameters = JsonSerializer.Deserialize<Dictionary<string, object>>(reqParams.GetRawText()) 
                    ?? new Dictionary<string, object>();
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse function selection response, using heuristic analysis");
            return AnalyzeFunctionHeuristically(response, availableFunctions);
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
    
    private FunctionSelectionResult AnalyzeFunctionHeuristically(string response, List<ToolDefinition> availableFunctions)
    {
        var result = new FunctionSelectionResult();
        var lowerResponse = response.ToLower();
        
        // 간단한 키워드 매칭으로 함수 선택
        var functionKeywords = new Dictionary<string, string[]>
        {
            ["list_directory"] = new[] { "list", "files", "directory", "folder" },
            ["read_file"] = new[] { "read", "open", "view", "show", "content" },
            ["write_file"] = new[] { "write", "create", "save", "file" },
            ["chat_response"] = new[] { "chat", "talk", "conversation", "discuss" }
        };
        
        foreach (var func in availableFunctions)
        {
            if (functionKeywords.TryGetValue(func.Name, out var keywords))
            {
                if (keywords.Any(keyword => lowerResponse.Contains(keyword)))
                {
                    result.PrimaryFunction = func.Name;
                    result.ConfidenceScore = 0.6;
                    result.Reasoning = $"Keyword match for {func.Name}";
                    break;
                }
            }
        }
        
        // 기본값 설정
        if (string.IsNullOrEmpty(result.PrimaryFunction))
        {
            result.PrimaryFunction = "chat_response";
            result.ConfidenceScore = 0.3;
            result.Reasoning = "Default to chat response";
        }
        
        return result;
    }
    
    private PhaseResult CreateErrorResult(string message)
    {
        return new PhaseResult
        {
            Phase = 2,
            Status = ExecutionStatus.Failure,
            ErrorMessage = message,
            Data = new Dictionary<string, object> { ["function_unknown"] = true }
        };
    }
}