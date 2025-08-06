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
    
    public int PhaseNumber => 2;
    
    public FunctionSelectionExecutor(
        ILogger<FunctionSelectionExecutor> logger,
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
            if (!state.PhaseHistory.TryGetValue(1, out var intentResult))
            {
                return CreateErrorResult("Intent analysis result not found");
            }
            
            var systemContext = await _contextProvider.FormatContextForPromptAsync(ContextLevel.Minimal);
            var availableFunctions = await _mcpClient.GetAvailableToolsAsync(cancellationToken);
            
            var prompt = BuildFunctionSelectionPrompt(systemContext, intentResult, availableFunctions);
            
            _logger.LogDebug("Phase 2: Selecting appropriate function");
            var response = await _llm.GenerateResponseAsync(prompt, [], cancellationToken);
            
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
        var functionsJson = JsonSerializer.Serialize(functions.Select(f => new {
            f.Name, 
            f.Description,
            Parameters = f.Parameters?.Select(p => new { p.Key, p.Value.Type, p.Value.Description, p.Value.Required })
        }), new JsonSerializerOptions { WriteIndented = true });
        
        var intentData = JsonSerializer.Serialize(intentResult.Data, new JsonSerializerOptions { WriteIndented = true });
        
        return $@"
{systemContext}

**ROLE**: Function Selector  
**TASK**: Select appropriate function considering current system context

**USER INTENT ANALYSIS**: {intentData}
**AVAILABLE FUNCTIONS**: {functionsJson}

**CONTEXT-AWARE SELECTION**:
- For time-based queries: Use current_time context
- For file operations: Consider working_directory context  
- For location queries: Check if location_info available
- For system operations: Consider current_os and permissions

**SELECTION CRITERIA**:
1. Function capability matches intent
2. Required context information is available  
3. System resources are sufficient
4. User permissions allow function execution

**SPECIAL CASES**:
- If intent_type is ""chat"": Select ""chat_response"" function
- If no suitable tool found: Select ""fallback_response"" function
- If multiple tools needed: Use execution_strategy ""sequential""

**RESPONSE FORMAT** (JSON only):
{{
    ""primary_function"": ""function_name"",
    ""secondary_functions"": [],
    ""execution_strategy"": ""single|sequential|parallel"",
    ""context_requirements"": {{
        ""time_context"": true/false,
        ""location_context"": true/false,
        ""system_context"": true/false
    }},
    ""required_parameters"": {{}},
    ""confidence_score"": 0.0,
    ""reasoning"": ""why this function with current context""
}}";
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