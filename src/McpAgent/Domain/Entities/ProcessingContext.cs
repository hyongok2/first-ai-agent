namespace McpAgent.Domain.Entities;

/// <summary>
/// 에이전트 처리 과정에서 사용되는 컨텍스트 정보
/// </summary>
public class ProcessingContext
{
    public string ConversationId { get; set; }
    public string OriginalInput { get; set; }
    public ProcessingPhase CurrentPhase { get; set; }
    public List<ProcessingStep> Steps { get; }
    public Dictionary<string, object> SessionData { get; }
    public DateTime StartedAt { get; set; }
    public DateTime LastUpdatedAt { get; private set; }
    
    // Additional properties needed by AgentOrchestrator
    public int TurnNumber { get; set; }
    public RefinedInput? RefinedInput { get; set; }
    public SystemCapability? SelectedCapability { get; set; }
    public string? ToolExecutionResults { get; set; }
    public string? FinalResponse { get; set; }
    public DateTime? CompletedAt { get; set; }

    public ProcessingContext()
    {
        Steps = new List<ProcessingStep>();
        SessionData = new Dictionary<string, object>();
        StartedAt = DateTime.UtcNow;
        LastUpdatedAt = DateTime.UtcNow;
        ConversationId = string.Empty;
        OriginalInput = string.Empty;
        CurrentPhase = ProcessingPhase.InputRefinement;
    }

    public ProcessingContext(string conversationId, string originalInput) : this()
    {
        ConversationId = conversationId ?? throw new ArgumentNullException(nameof(conversationId));
        OriginalInput = originalInput ?? throw new ArgumentNullException(nameof(originalInput));
    }

    public void AdvanceToPhase(ProcessingPhase phase)
    {
        CurrentPhase = phase;
        LastUpdatedAt = DateTime.UtcNow;
    }

    public void AddStep(ProcessingStep step)
    {
        Steps.Add(step);
        LastUpdatedAt = DateTime.UtcNow;
    }

    public T? GetSessionData<T>(string key) where T : class
    {
        return SessionData.TryGetValue(key, out var value) ? value as T : null;
    }

    public void SetSessionData<T>(string key, T value) where T : notnull
    {
        SessionData[key] = value;
        LastUpdatedAt = DateTime.UtcNow;
    }
}

/// <summary>
/// 처리 과정의 개별 단계 정보
/// </summary>
public class ProcessingStep
{
    public ProcessingPhase Phase { get; }
    public string Description { get; }
    public object? Input { get; }
    public object? Output { get; }
    public bool IsSuccess { get; }
    public string? Error { get; }
    public DateTime ExecutedAt { get; }
    public TimeSpan Duration { get; }

    public ProcessingStep(
        ProcessingPhase phase,
        string description,
        object? input = null,
        object? output = null,
        bool isSuccess = true,
        string? error = null,
        TimeSpan? duration = null)
    {
        Phase = phase;
        Description = description ?? throw new ArgumentNullException(nameof(description));
        Input = input;
        Output = output;
        IsSuccess = isSuccess;
        Error = error;
        ExecutedAt = DateTime.UtcNow;
        Duration = duration ?? TimeSpan.Zero;
    }
}