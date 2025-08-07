namespace McpAgent.Domain.Entities;

/// <summary>
/// LLM에 의해 정제 및 정리된 사용자 입력
/// </summary>
public class RefinedInput
{
    public string OriginalInput { get; }
    public string ClarifiedIntent { get; }
    public string RefinedQuery { get; }
    public List<string> ExtractedEntities { get; }
    public Dictionary<string, object> Context { get; }
    public string? SuggestedPlan { get; }
    public ConfidenceLevel IntentConfidence { get; }
    public double ConfidenceScore => (double)IntentConfidence / 4.0; // Convert enum to 0-1 scale
    public bool RequiresFollowUp => IntentConfidence <= ConfidenceLevel.Low; // Auto-determine based on confidence
    public DateTime ProcessedAt { get; }

    public RefinedInput(
        string originalInput,
        string clarifiedIntent,
        string refinedQuery,
        List<string>? extractedEntities = null,
        Dictionary<string, object>? context = null,
        string? suggestedPlan = null,
        ConfidenceLevel intentConfidence = ConfidenceLevel.Medium)
    {
        OriginalInput = originalInput ?? throw new ArgumentNullException(nameof(originalInput));
        ClarifiedIntent = clarifiedIntent ?? throw new ArgumentNullException(nameof(clarifiedIntent));
        RefinedQuery = refinedQuery ?? throw new ArgumentNullException(nameof(refinedQuery));
        ExtractedEntities = extractedEntities ?? new List<string>();
        Context = context ?? new Dictionary<string, object>();
        SuggestedPlan = suggestedPlan;
        IntentConfidence = intentConfidence;
        ProcessedAt = DateTime.UtcNow;
    }
}

/// <summary>
/// 의도 파악 신뢰도 수준
/// </summary>
public enum ConfidenceLevel
{
    /// <summary>매우 낮음 - 추가 명확화 필요</summary>
    VeryLow,
    
    /// <summary>낮음 - 일부 불확실성 존재</summary>
    Low,
    
    /// <summary>보통 - 기본적인 이해 가능</summary>
    Medium,
    
    /// <summary>높음 - 명확한 이해</summary>
    High,
    
    /// <summary>매우 높음 - 완전한 이해</summary>
    VeryHigh
}