using System.Text.Json;
using McpAgent.Models;
using McpAgent.Providers;
using McpAgent.Services;
using Microsoft.Extensions.Logging;

namespace McpAgent.Core.PhaseExecutors;

public class IntentAnalysisExecutor : IPhaseExecutor
{
    private readonly ILogger<IntentAnalysisExecutor> _logger;
    private readonly ILlmProvider _llm;
    private readonly ISystemContextProvider _contextProvider;
    
    public int PhaseNumber => 1;
    
    public IntentAnalysisExecutor(
        ILogger<IntentAnalysisExecutor> logger,
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
            var systemContext = await _contextProvider.FormatContextForPromptAsync(ContextLevel.Standard);
            var chatSummary = BuildChatSummary(state);
            
            var prompt = BuildIntentAnalysisPrompt(systemContext, userInput, chatSummary);
            
            _logger.LogDebug("Phase 1: Analyzing user intent");
            var response = await _llm.GenerateResponseAsync(prompt, [], cancellationToken);
            
            var parsed = ParseIntentResponse(response);
            
            return new PhaseResult
            {
                Phase = 1,
                Status = parsed.ConfidenceScore >= 0.7 ? ExecutionStatus.Success : ExecutionStatus.RequiresInput,
                Data = parsed.ToDictionary(),
                ConfidenceScore = parsed.ConfidenceScore,
                RequiresUserInput = parsed.ClarificationQuestions.Any(),
                Messages = parsed.ClarificationQuestions
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in intent analysis phase");
            return new PhaseResult
            {
                Phase = 1,
                Status = ExecutionStatus.Failure,
                ErrorMessage = ex.Message,
                Data = new Dictionary<string, object> { ["clarification_needed"] = true }
            };
        }
    }
    
    private string BuildIntentAnalysisPrompt(string systemContext, string userInput, string chatSummary)
    {
        return $@"
{systemContext}

**ROLE**: Intent Analyzer
**TASK**: Analyze user intent with current context awareness

**USER MESSAGE**: {userInput}
**CONVERSATION HISTORY**: {chatSummary}

**CONTEXT-AWARE ANALYSIS**:
Consider the current time and date when interpreting requests like:
- ""오늘"", ""내일"", ""이번 주"" → Use current date context
- ""지금"", ""현재"" → Use current time context  
- Location-based requests → Use available location info
- ""업무 시간"", ""주말"" → Consider current day of week

**INSTRUCTIONS**:
1. Classify intent: chat, tool_usage, complex_task, clarification_needed
2. Consider temporal context (is this time-sensitive?)
3. Consider location context (if location-based request)
4. Provide confidence score (0.0-1.0)
5. Suggest clarification if needed

**INTENT ANALYSIS JSON FORMAT** (CRITICAL - Follow exactly):

**REQUIRED JSON STRUCTURE**:
{{
    ""intent_type"": ""chat|tool_usage|complex_task|clarification_needed"",
    ""confidence_score"": 0.85,
    ""temporal_context"": ""none"",
    ""location_context"": ""none"", 
    ""clarification_questions"": [""question1"", ""question2""],
    ""estimated_complexity"": ""simple"",
    ""reasoning"": ""detailed explanation with context considerations""
}}

**JSON FORMAT RULES**:
- ALL keys in double quotes: ""intent_type""
- String values in double quotes: ""chat""
- Numbers without quotes: 0.85
- Arrays with square brackets: [""question1""]
- Empty arrays: []

**VALID VALUES**:
- intent_type: ""chat"", ""tool_usage"", ""complex_task"", ""clarification_needed""
- temporal_context: ""none"", ""time_sensitive"", ""date_specific""
- location_context: ""none"", ""location_required"", ""location_helpful""
- estimated_complexity: ""simple"", ""moderate"", ""complex""
- confidence_score: 0.0 to 1.0

**COMPLETE REAL EXAMPLES**:

Simple chat request:
{{
    ""intent_type"": ""chat"",
    ""confidence_score"": 0.9,
    ""temporal_context"": ""none"",
    ""location_context"": ""none"",
    ""clarification_questions"": [],
    ""estimated_complexity"": ""simple"",
    ""reasoning"": ""User greeting or general question, no tool usage required""
}}

File operation request:
{{
    ""intent_type"": ""tool_usage"",
    ""confidence_score"": 0.95,
    ""temporal_context"": ""none"",
    ""location_context"": ""none"",
    ""clarification_questions"": [],
    ""estimated_complexity"": ""simple"",
    ""reasoning"": ""Clear request to read/write files, tools available and context sufficient""
}}

Time-sensitive request:
{{
    ""intent_type"": ""tool_usage"",
    ""confidence_score"": 0.8,
    ""temporal_context"": ""time_sensitive"",
    ""location_context"": ""none"",
    ""clarification_questions"": [],
    ""estimated_complexity"": ""moderate"",
    ""reasoning"": ""Request mentions 'now' or 'today', requires current time context""
}}

Unclear request needing clarification:
{{
    ""intent_type"": ""clarification_needed"",
    ""confidence_score"": 0.3,
    ""temporal_context"": ""none"",
    ""location_context"": ""none"",
    ""clarification_questions"": [""Which file do you want to read?"", ""What specific information are you looking for?""],
    ""estimated_complexity"": ""simple"",
    ""reasoning"": ""User request is ambiguous, need more details before proceeding""
}}";
    }
    
    private string BuildChatSummary(ConversationState state)
    {
        if (!state.UserContext.RecentQueries.Any())
            return "No previous conversation";
            
        var recent = state.UserContext.RecentQueries.TakeLast(3);
        return $"Recent topics: {string.Join(", ", recent)}";
    }
    
    private IntentAnalysisResult ParseIntentResponse(string response)
    {
        try
        {
            // JSON 응답 파싱 시도
            var cleanResponse = ExtractJsonFromResponse(response);
            var parsed = JsonSerializer.Deserialize<JsonElement>(cleanResponse);
            
            return new IntentAnalysisResult
            {
                IntentType = parsed.TryGetProperty("intent_type", out var intentType) ? 
                    intentType.GetString() ?? "unknown" : "unknown",
                ConfidenceScore = parsed.TryGetProperty("confidence_score", out var confidence) ? 
                    confidence.GetDouble() : 0.3,
                TemporalContext = parsed.TryGetProperty("temporal_context", out var temporal) ? 
                    temporal.GetString() ?? "none" : "none",
                LocationContext = parsed.TryGetProperty("location_context", out var location) ? 
                    location.GetString() ?? "none" : "none",
                ClarificationQuestions = parsed.TryGetProperty("clarification_questions", out var questions) ? 
                    questions.EnumerateArray().Select(q => q.GetString() ?? "").Where(q => !string.IsNullOrEmpty(q)).ToList() : new(),
                EstimatedComplexity = parsed.TryGetProperty("estimated_complexity", out var complexity) ? 
                    complexity.GetString() ?? "simple" : "simple",
                Reasoning = parsed.TryGetProperty("reasoning", out var reasoning) ? 
                    reasoning.GetString() ?? "" : ""
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse JSON response, using heuristic analysis");
            return AnalyzeIntentHeuristically(response);
        }
    }
    
    private string ExtractJsonFromResponse(string response)
    {
        // JSON 블록 추출
        var startIndex = response.IndexOf('{');
        var endIndex = response.LastIndexOf('}');
        
        if (startIndex >= 0 && endIndex > startIndex)
        {
            return response.Substring(startIndex, endIndex - startIndex + 1);
        }
        
        return response;
    }
    
    private IntentAnalysisResult AnalyzeIntentHeuristically(string response)
    {
        var lowerResponse = response.ToLower();
        var result = new IntentAnalysisResult();
        
        // 의도 추정
        if (lowerResponse.Contains("tool") || lowerResponse.Contains("file") || lowerResponse.Contains("search") || lowerResponse.Contains("execute"))
        {
            result.IntentType = "tool_usage";
            result.ConfidenceScore = 0.6;
        }
        else if (lowerResponse.Contains("complex") || lowerResponse.Contains("multiple") || lowerResponse.Contains("step"))
        {
            result.IntentType = "complex_task";
            result.ConfidenceScore = 0.5;
        }
        else if (lowerResponse.Contains("clarify") || lowerResponse.Contains("unclear") || lowerResponse.Contains("?"))
        {
            result.IntentType = "clarification_needed";
            result.ConfidenceScore = 0.4;
            result.ClarificationQuestions.Add("Could you please provide more details?");
        }
        else
        {
            result.IntentType = "chat";
            result.ConfidenceScore = 0.7;
        }
        
        // 시간 컨텍스트 추정
        if (lowerResponse.Contains("now") || lowerResponse.Contains("today") || lowerResponse.Contains("time"))
        {
            result.TemporalContext = "time_sensitive";
        }
        
        // 위치 컨텍스트 추정
        if (lowerResponse.Contains("location") || lowerResponse.Contains("near") || lowerResponse.Contains("here"))
        {
            result.LocationContext = "location_helpful";
        }
        
        result.Reasoning = "Heuristic analysis based on keywords";
        return result;
    }
}