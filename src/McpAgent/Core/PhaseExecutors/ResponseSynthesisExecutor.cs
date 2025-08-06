using System.Text.Json;
using McpAgent.Models;
using McpAgent.Providers;
using McpAgent.Services;
using Microsoft.Extensions.Logging;

namespace McpAgent.Core.PhaseExecutors;

public class ResponseSynthesisExecutor : IPhaseExecutor
{
    private readonly ILogger<ResponseSynthesisExecutor> _logger;
    private readonly ILlmProvider _llm;
    private readonly ISystemContextProvider _contextProvider;
    
    public int PhaseNumber => 5;
    
    public ResponseSynthesisExecutor(
        ILogger<ResponseSynthesisExecutor> logger,
        ILlmProvider llm,
        ISystemContextProvider contextProvider)
    {
        _logger = logger;
        _llm = llm;
        _contextProvider = contextProvider;
    }
    
    public async Task<PhaseResult> ExecuteAsync(ConversationState state, string userInput, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!state.PhaseHistory.TryGetValue(4, out var executionResult))
            {
                return CreateErrorResult("Tool execution result not found");
            }
            
            // 실행 결과가 있는지 확인
            var executionResults = ExtractExecutionResults(executionResult);
            if (!executionResults.Any())
            {
                return CreateSimpleResponse("I was unable to process your request properly.");
            }
            
            var systemContext = await _contextProvider.FormatContextForPromptAsync(ContextLevel.Minimal);
            var prompt = BuildResponseSynthesisPrompt(systemContext, userInput, state, executionResults);
            
            _logger.LogDebug("Phase 5: Synthesizing final response");
            var response = await _llm.GenerateResponseAsync(prompt, [], cancellationToken);
            
            var parsed = ParseResponseSynthesis(response);
            
            return new PhaseResult
            {
                Phase = 5,
                Status = ExecutionStatus.Success,
                Data = parsed.ToDictionary(),
                ConfidenceScore = 1.0,
                RequiresUserInput = parsed.ConversationStatus == "awaiting_input"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in response synthesis phase");
            return CreateSimpleResponse("I encountered an error while processing your request. Please try again.");
        }
    }
    
    private string BuildResponseSynthesisPrompt(string systemContext, string userInput, ConversationState state, List<object> executionResults)
    {
        var resultsJson = JsonSerializer.Serialize(executionResults, new JsonSerializerOptions { WriteIndented = true });
        
        // 이전 단계들의 컨텍스트 수집
        var intentData = state.PhaseHistory.GetValueOrDefault(1)?.Data ?? new Dictionary<string, object>();
        var functionData = state.PhaseHistory.GetValueOrDefault(2)?.Data ?? new Dictionary<string, object>();
        
        var contextSummary = JsonSerializer.Serialize(new
        {
            original_intent = intentData.GetValueOrDefault("intent_type"),
            selected_function = functionData.GetValueOrDefault("primary_function"),
            execution_summary = $"{executionResults.Count} tool(s) executed"
        });
        
        return $@"
{systemContext}

**ROLE**: Response Synthesizer
**TASK**: Create natural, helpful response from execution results

**ORIGINAL USER INPUT**: {userInput}
**PROCESSING CONTEXT**: {contextSummary}
**EXECUTION RESULTS**: {resultsJson}

**RESPONSE GUIDELINES**:
1. Create a natural, conversational response
2. Summarize what was accomplished
3. Present results in user-friendly format
4. Suggest logical follow-up actions if appropriate
5. Be concise but informative
6. If errors occurred, explain what went wrong and suggest alternatives

**SPECIAL CASES**:
- If chat_response was executed: Use the result directly with minor enhancement
- If file operations: Describe what files were accessed/created
- If errors occurred: Be empathetic and provide helpful guidance
- If multiple tools used: Summarize the workflow completed

**RESPONSE FORMAT** (JSON only):
{{
    ""natural_response"": ""User-friendly response explaining results"",
    ""follow_up_suggestions"": [
        ""suggestion 1"",
        ""suggestion 2""
    ],
    ""conversation_status"": ""complete|awaiting_input|continue_task"",
    ""next_phase_hint"": 1,
    ""summary"": ""Brief summary of what was accomplished""
}}";
    }
    
    private List<object> ExtractExecutionResults(PhaseResult executionResult)
    {
        if (executionResult.Data.TryGetValue("execution_results", out var resultsObj) &&
            resultsObj is List<object> resultsList)
        {
            return resultsList;
        }
        
        return new List<object>();
    }
    
    private ResponseSynthesisResult ParseResponseSynthesis(string response)
    {
        try
        {
            var cleanResponse = ExtractJsonFromResponse(response);
            var parsed = JsonSerializer.Deserialize<JsonElement>(cleanResponse);
            
            var result = new ResponseSynthesisResult
            {
                NaturalResponse = parsed.TryGetProperty("natural_response", out var naturalResp) ? 
                    naturalResp.GetString() ?? response : response,
                ConversationStatus = parsed.TryGetProperty("conversation_status", out var status) ? 
                    status.GetString() ?? "complete" : "complete",
                Summary = parsed.TryGetProperty("summary", out var summary) ? 
                    summary.GetString() ?? "" : ""
            };
            
            // Follow-up suggestions 파싱
            if (parsed.TryGetProperty("follow_up_suggestions", out var suggestions))
            {
                result.FollowUpSuggestions = suggestions.EnumerateArray()
                    .Select(s => s.GetString() ?? "")
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
            }
            
            // Next phase hint 파싱
            if (parsed.TryGetProperty("next_phase_hint", out var nextPhase))
            {
                result.NextPhaseHint = nextPhase.GetInt32();
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse response synthesis, using raw response");
            
            // 파싱 실패시 원본 응답 사용
            return new ResponseSynthesisResult
            {
                NaturalResponse = response,
                ConversationStatus = "complete",
                Summary = "Response generated"
            };
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
    
    private PhaseResult CreateErrorResult(string message)
    {
        return new PhaseResult
        {
            Phase = 5,
            Status = ExecutionStatus.Failure,
            ErrorMessage = message,
            Data = new Dictionary<string, object> { ["natural_response"] = message }
        };
    }
    
    private PhaseResult CreateSimpleResponse(string message)
    {
        return new PhaseResult
        {
            Phase = 5,
            Status = ExecutionStatus.Success,
            Data = new Dictionary<string, object>
            {
                ["natural_response"] = message,
                ["conversation_status"] = "complete",
                ["summary"] = "Simple response generated"
            },
            ConfidenceScore = 0.8
        };
    }
}

public class ResponseSynthesisResult
{
    public string NaturalResponse { get; set; } = string.Empty;
    public List<string> FollowUpSuggestions { get; set; } = new();
    public string ConversationStatus { get; set; } = "complete";
    public int NextPhaseHint { get; set; } = 1;
    public string Summary { get; set; } = string.Empty;
    
    public Dictionary<string, object> ToDictionary()
    {
        var dict = new Dictionary<string, object>
        {
            ["natural_response"] = NaturalResponse,
            ["follow_up_suggestions"] = FollowUpSuggestions,
            ["conversation_status"] = ConversationStatus,
            ["summary"] = Summary
        };
        
        if (ConversationStatus == "continue_task")
        {
            dict["next_phase"] = NextPhaseHint;
        }
        
        return dict;
    }
}