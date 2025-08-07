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
    private readonly IDebugFileLogger _debugLogger;
    
    public int PhaseNumber => 1;
    
    public IntentAnalysisExecutor(
        ILogger<IntentAnalysisExecutor> logger,
        ILlmProvider llm,
        ISystemContextProvider contextProvider,
        IDebugFileLogger debugLogger)
    {
        _logger = logger;
        _llm = llm;
        _contextProvider = contextProvider;
        _debugLogger = debugLogger;
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
            
            // Debug logging for prompt and response
            await _debugLogger.LogPromptAndResponseAsync(prompt, response, "intent-analysis");
            
            var parsed = ParseIntentResponse(response, userInput);
            
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

**TOOL USAGE DETECTION RULES**:
- If the request can be fulfilled by ANY available tool, classify as ""tool_usage""
- ""Database"", ""DB"", ""Oracle"", ""테이블"", ""스키마"" → Use OracleDbTools
- ""Connection test"", ""연결 테스트"" → Use OracleDbTools_TestConnection
- ""Database info"", ""DB info"", ""스키마 정보"", ""테이블 정보"" → Use OracleDbTools_GetDatabaseInfo
- ""Query"", ""SELECT"", ""조회"" → Use OracleDbTools_Query
- ""Echo"", ""반복"", ""메아리"" → Use Echo_Echo

**CLARIFICATION RULES**:
- Only use ""clarification_needed"" if:
  1. The request is genuinely ambiguous AND
  2. NO available tool can reasonably fulfill the request AND
  3. Multiple interpretations are equally possible
- If ANY tool matches the request, use ""tool_usage"" instead

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

Database information request:
{{
    ""intent_type"": ""tool_usage"",
    ""confidence_score"": 0.95,
    ""temporal_context"": ""none"",
    ""location_context"": ""none"",
    ""clarification_questions"": [],
    ""estimated_complexity"": ""simple"",
    ""reasoning"": ""Clear request for database information. OracleDbTools_GetDatabaseInfo tool is available and matches exactly.""
}}

Oracle DB Info request:
{{
    ""intent_type"": ""tool_usage"",
    ""confidence_score"": 0.9,
    ""temporal_context"": ""none"",
    ""location_context"": ""none"",
    ""clarification_questions"": [],
    ""estimated_complexity"": ""simple"",
    ""reasoning"": ""User requests Oracle database info, which directly maps to OracleDbTools_GetDatabaseInfo tool.""
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
    
    private IntentAnalysisResult ParseIntentResponse(string response, string userInput)
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
            return AnalyzeIntentHeuristically(response, userInput);
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
    
    private IntentAnalysisResult AnalyzeIntentHeuristically(string response, string userInput)
    {
        var lowerResponse = response.ToLower();
        var lowerUserInput = userInput.ToLower();
        var result = new IntentAnalysisResult();
        
        // 사용자 입력에서 직접 데이터베이스 관련 키워드 우선 처리
        if (lowerUserInput.Contains("database") || lowerUserInput.Contains("db") || 
            lowerUserInput.Contains("oracle") || lowerUserInput.Contains("테이블") ||
            lowerUserInput.Contains("스키마") || lowerUserInput.Contains("connection") ||
            lowerUserInput.Contains("query") || lowerUserInput.Contains("select") ||
            lowerUserInput.Contains("info") || lowerUserInput.Contains("정보"))
        {
            result.IntentType = "tool_usage";
            result.ConfidenceScore = 0.9;
        }
        // 응답에서 데이터베이스 관련 키워드 처리
        else if (lowerResponse.Contains("database") || lowerResponse.Contains("db") || 
            lowerResponse.Contains("oracle") || lowerResponse.Contains("테이블") ||
            lowerResponse.Contains("스키마") || lowerResponse.Contains("connection") ||
            lowerResponse.Contains("query") || lowerResponse.Contains("select"))
        {
            result.IntentType = "tool_usage";
            result.ConfidenceScore = 0.85;
        }
        else if (lowerResponse.Contains("tool") || lowerResponse.Contains("file") || 
                 lowerResponse.Contains("search") || lowerResponse.Contains("execute"))
        {
            result.IntentType = "tool_usage";
            result.ConfidenceScore = 0.7;
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