namespace McpAgent.Models;

public class PhaseResult
{
    public int Phase { get; set; }
    public ExecutionStatus Status { get; set; } = ExecutionStatus.Success;
    public Dictionary<string, object> Data { get; set; } = new();
    public double ConfidenceScore { get; set; } = 1.0;
    public bool RequiresUserInput { get; set; } = false;
    public List<string> Messages { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public enum ExecutionStatus
{
    Success,
    PartialSuccess,
    Failure,
    NeedsRetry,
    RequiresInput
}

public class ConversationState
{
    public string ConversationId { get; set; } = string.Empty;
    public int CurrentPhase { get; set; } = 1;
    public Dictionary<int, PhaseResult> PhaseHistory { get; set; } = new();
    public LoopContext LoopContext { get; set; } = new();
    public UserContext UserContext { get; set; } = new();
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    
    public bool ShouldLoop(int fromPhase, PhaseResult result)
    {
        return LoopContext.ShouldLoop(fromPhase, result);
    }
    
    public int GetNextPhase(int currentPhase, PhaseResult result)
    {
        return LoopContext.DetermineNextPhase(currentPhase, result);
    }
}

public class LoopContext
{
    public int MaxLoopIterations { get; set; } = 5;
    public Dictionary<int, int> PhaseLoopCounts { get; set; } = new();
    public List<LoopDecision> LoopHistory { get; set; } = new();
    
    public bool ShouldLoop(int fromPhase, PhaseResult result)
    {
        var loopCount = PhaseLoopCounts.GetValueOrDefault(fromPhase, 0);
        
        // 최대 반복 횟수 체크
        if (loopCount >= MaxLoopIterations)
        {
            return false;
        }
        
        // Success 상태면 루프하지 않음 (핵심 수정)
        if (result.Status == ExecutionStatus.Success && !result.RequiresUserInput)
        {
            return false;
        }
        
        // 단계별 루프 조건 체크
        return fromPhase switch
        {
            1 => result.ConfidenceScore < 0.7 || result.Data.ContainsKey("clarification_needed"),
            2 => result.Data.ContainsKey("function_unknown") || result.ConfidenceScore < 0.6,
            3 => result.Data.ContainsKey("missing_parameters") || result.ConfidenceScore < 0.8,
            4 => result.Status == ExecutionStatus.NeedsRetry || result.Status == ExecutionStatus.PartialSuccess,
            5 => result.Data.ContainsKey("continue_task"),
            _ => false
        };
    }
    
    public int DetermineNextPhase(int currentPhase, PhaseResult result)
    {
        // 루프가 필요하지 않으면 다음 단계
        if (!ShouldLoop(currentPhase, result))
        {
            return Math.Min(currentPhase + 1, 5);
        }
        
        // 루프 로직
        return currentPhase switch
        {
            1 => 1, // 의도 재분석
            2 when result.Data.ContainsKey("need_intent_clarity") => 1, // 의도부터 다시
            2 => 2, // 기능 재선택
            3 when result.Data.ContainsKey("invalid_function") => 2, // 기능 재선택
            3 => 3, // 파라미터 재생성
            4 when result.Data.ContainsKey("wrong_function") => 2, // 다른 기능 선택
            4 => 3, // 파라미터 재조정
            5 when result.Data.ContainsKey("next_phase") => (int)result.Data["next_phase"],
            _ => currentPhase + 1
        };
    }
}

public class LoopDecision
{
    public int FromPhase { get; set; }
    public int ToPhase { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public class UserContext
{
    public string? CurrentTask { get; set; }
    public List<string> RecentQueries { get; set; } = new();
    public Dictionary<string, object> Preferences { get; set; } = new();
    public string? LastUserInput { get; set; }
}

// Phase별 결과 모델들
public class IntentAnalysisResult
{
    public string IntentType { get; set; } = "unknown";
    public double ConfidenceScore { get; set; }
    public string TemporalContext { get; set; } = "none";
    public string LocationContext { get; set; } = "none";
    public List<string> ClarificationQuestions { get; set; } = new();
    public string EstimatedComplexity { get; set; } = "simple";
    public string Reasoning { get; set; } = string.Empty;
    
    public Dictionary<string, object> ToDictionary()
    {
        return new Dictionary<string, object>
        {
            ["intent_type"] = IntentType,
            ["confidence_score"] = ConfidenceScore,
            ["temporal_context"] = TemporalContext,
            ["location_context"] = LocationContext,
            ["clarification_questions"] = ClarificationQuestions,
            ["estimated_complexity"] = EstimatedComplexity,
            ["reasoning"] = Reasoning,
            ["clarification_needed"] = ClarificationQuestions.Count > 0
        };
    }
}

public class FunctionSelectionResult
{
    public string PrimaryFunction { get; set; } = string.Empty;
    public List<string> SecondaryFunctions { get; set; } = new();
    public string ExecutionStrategy { get; set; } = "single";
    public Dictionary<string, bool> ContextRequirements { get; set; } = new();
    public Dictionary<string, object> RequiredParameters { get; set; } = new();
    public double ConfidenceScore { get; set; }
    public string Reasoning { get; set; } = string.Empty;
    public bool IsValid => !string.IsNullOrEmpty(PrimaryFunction) && PrimaryFunction != "unknown";
    
    public Dictionary<string, object> ToDictionary()
    {
        return new Dictionary<string, object>
        {
            ["primary_function"] = PrimaryFunction,
            ["secondary_functions"] = SecondaryFunctions,
            ["execution_strategy"] = ExecutionStrategy,
            ["context_requirements"] = ContextRequirements,
            ["required_parameters"] = RequiredParameters,
            ["confidence_score"] = ConfidenceScore,
            ["reasoning"] = Reasoning,
            ["function_unknown"] = !IsValid
        };
    }
}