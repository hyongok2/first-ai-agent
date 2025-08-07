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
    
    /// <summary>
    /// 대화 완료 후 다음 요청을 위해 상태 초기화 (계층적 요약 시스템)
    /// </summary>
    public void PrepareForNewConversation()
    {
        // 이전 대화 요약을 ConversationSummaries에 저장
        if (PhaseHistory.Any())
        {
            var lastResult = PhaseHistory.Values.LastOrDefault();
            var conversationSummary = lastResult?.Data.GetValueOrDefault("summary")?.ToString() ?? 
                                     $"Previous conversation completed at {LastActivity:HH:mm}";
            
            // 사용한 도구들 추출
            var toolsUsed = new List<string>();
            var keyResults = new Dictionary<string, string>();
            
            if (PhaseHistory.TryGetValue(2, out var functionPhase))
            {
                var primaryFunction = functionPhase.Data.GetValueOrDefault("primary_function")?.ToString();
                if (!string.IsNullOrEmpty(primaryFunction))
                    toolsUsed.Add(primaryFunction);
            }
            
            if (PhaseHistory.TryGetValue(4, out var executionPhase))
            {
                // 실행 결과에서 핵심 데이터 추출
                ExtractKeyResults(executionPhase.Data, keyResults);
            }
            
            // 새로운 대화 요약 생성
            var newSummary = new ConversationSummary
            {
                Summary = conversationSummary,
                CreatedAt = LastActivity,
                UserRequest = UserContext.LastUserInput ?? "Unknown request",
                ToolsUsed = toolsUsed,
                KeyResults = keyResults
            };
            
            // 계층적 요약 관리 (7개 임계값)
            ManageHierarchicalSummaries(newSummary);
        }
        
        // 새 대화를 위해 상태 초기화
        CurrentPhase = 1;
        PhaseHistory.Clear();
        LoopContext = new LoopContext();
        LastActivity = DateTime.UtcNow;
        
        // CurrentTask는 유지하지만 LastUserInput은 초기화
        UserContext.LastUserInput = null;
    }
    
    /// <summary>
    /// 토큰 기반 적응형 계층적 요약 관리
    /// </summary>
    private void ManageHierarchicalSummaries(ConversationSummary newSummary)
    {
        // 새 요약 추가
        UserContext.ConversationSummaries.Add(newSummary);
        
        // 토큰 기반 임계값 결정 (기본 7개, 토큰에 따라 3-10개 가변)
        var threshold = DetermineAdaptiveThreshold();
        
        // 임계값 이상이면 메타 요약 생성 및 압축
        if (UserContext.ConversationSummaries.Count >= threshold)
        {
            // 기존 메타 요약과 현재 요약들을 결합하여 새로운 메타 요약 생성
            // (실제 LLM 호출은 ResponseSynthesisExecutor에서 처리하도록 개선 예정)
            
            var allSummaries = UserContext.ConversationSummaries.Select(s => s.Summary).ToList();
            if (!string.IsNullOrEmpty(UserContext.MetaSummary))
            {
                allSummaries.Insert(0, UserContext.MetaSummary);
            }
            
            // 토큰 제한에 맞게 메타 요약 생성
            var combinedSummary = string.Join("; ", allSummaries);
            if (combinedSummary.Length > 1000) // 대략적인 토큰 제한 (추후 정확한 계산으로 개선)
            {
                // 간단한 압축: 앞의 3개와 뒤의 3개만 유지
                var importantSummaries = allSummaries.Take(3).Concat(allSummaries.TakeLast(3)).Distinct();
                combinedSummary = string.Join("; ", importantSummaries);
            }
            
            UserContext.MetaSummary = $"Previous sessions: {combinedSummary}";
            UserContext.MetaSummaryCreatedAt = DateTime.UtcNow;
            
            // 개별 요약들 클리어 (메타 요약으로 압축됨)
            UserContext.ConversationSummaries.Clear();
        }
        
        // RecentQueries는 계속 유지하되 크기 제한
        UserContext.RecentQueries.Add(newSummary.UserRequest);
        if (UserContext.RecentQueries.Count > 10)
        {
            UserContext.RecentQueries.RemoveAt(0);
        }
    }
    
    /// <summary>
    /// 컨텍스트 윈도우 크기에 따른 적응형 임계값 결정
    /// </summary>
    private int DetermineAdaptiveThreshold()
    {
        // 현재는 간단한 휴리스틱 사용 (추후 TokenCalculationService 연동)
        // 메타 요약 길이에 따라 임계값 조정
        if (!string.IsNullOrEmpty(UserContext.MetaSummary))
        {
            var metaLength = UserContext.MetaSummary.Length;
            return metaLength switch
            {
                > 2000 => 3,  // 메타 요약이 매우 길면 빨리 압축
                > 1000 => 5,  // 메타 요약이 길면 조금 빨리 압축
                > 500 => 7,   // 기본값
                _ => 10       // 메타 요약이 짧으면 더 많이 유지
            };
        }
        
        return 7; // 기본값
    }
    
    /// <summary>
    /// 실행 결과에서 핵심 데이터 추출
    /// </summary>
    private void ExtractKeyResults(Dictionary<string, object> executionData, Dictionary<string, string> keyResults)
    {
        if (executionData.TryGetValue("execution_results", out var resultsObj) && 
            resultsObj is List<object> resultsList)
        {
            foreach (var result in resultsList.Take(3)) // 최대 3개 주요 결과만
            {
                try
                {
                    var resultStr = result.ToString();
                    if (!string.IsNullOrEmpty(resultStr) && resultStr.Contains("success"))
                    {
                        keyResults[$"result_{keyResults.Count + 1}"] = resultStr.Substring(0, Math.Min(100, resultStr.Length));
                    }
                }
                catch
                {
                    // 결과 파싱 실패는 무시
                }
            }
        }
    }
    
    /// <summary>
    /// 대화가 완료되었는지 확인
    /// </summary>
    public bool IsConversationCompleted()
    {
        return CurrentPhase == 5 && 
               PhaseHistory.ContainsKey(5) && 
               PhaseHistory[5].Status == ExecutionStatus.Success &&
               !PhaseHistory[5].RequiresUserInput;
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
    
    /// <summary>
    /// 개별 대화 요약들 (최대 7개)
    /// </summary>
    public List<ConversationSummary> ConversationSummaries { get; set; } = new();
    
    /// <summary>
    /// 메타 요약 (7개 이상의 대화를 압축한 요약)
    /// </summary>
    public string? MetaSummary { get; set; }
    
    /// <summary>
    /// 메타 요약이 생성된 날짜
    /// </summary>
    public DateTime? MetaSummaryCreatedAt { get; set; }
}

/// <summary>
/// 개별 대화 세션의 요약 정보
/// </summary>
public class ConversationSummary
{
    public string Summary { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string UserRequest { get; set; } = string.Empty;
    public List<string> ToolsUsed { get; set; } = new();
    public Dictionary<string, string> KeyResults { get; set; } = new();
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