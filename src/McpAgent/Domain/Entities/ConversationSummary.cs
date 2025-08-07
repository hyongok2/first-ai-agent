namespace McpAgent.Domain.Entities;

/// <summary>
/// 대화 세션의 요약 정보를 관리합니다.
/// 5턴까지는 개별 요약, 이후에는 통합 요약으로 관리됩니다.
/// </summary>
public class ConversationSummary
{
    public string ConversationId { get; }
    public List<TurnSummary> IndividualTurns { get; }
    public string? ConsolidatedSummary { get; private set; }
    public int TotalTurns { get; private set; }
    public DateTime CreatedAt { get; }
    public DateTime LastUpdatedAt { get; private set; }

    public ConversationSummary(string conversationId)
    {
        ConversationId = conversationId ?? throw new ArgumentNullException(nameof(conversationId));
        IndividualTurns = new List<TurnSummary>();
        CreatedAt = DateTime.UtcNow;
        LastUpdatedAt = DateTime.UtcNow;
        TotalTurns = 0;
    }

    public void AddTurnSummary(TurnSummary turnSummary)
    {
        TotalTurns++;
        
        if (IndividualTurns.Count < 5)
        {
            // 5턴까지는 개별 요약으로 관리
            IndividualTurns.Add(turnSummary);
        }
        else
        {
            // 5턴 초과시 통합 요약 업데이트
            ConsolidatedSummary = turnSummary.OverallSummary;
        }
        
        LastUpdatedAt = DateTime.UtcNow;
    }

    public void SetConsolidatedSummary(string consolidatedSummary)
    {
        ConsolidatedSummary = consolidatedSummary;
        
        // 통합 요약이 생성되면 개별 요약은 클리어 (메모리 절약)
        IndividualTurns.Clear();
        LastUpdatedAt = DateTime.UtcNow;
    }

    public string GetContextForNewTurn()
    {
        if (!string.IsNullOrEmpty(ConsolidatedSummary))
        {
            return ConsolidatedSummary;
        }

        if (IndividualTurns.Any())
        {
            return string.Join("\n\n", IndividualTurns.Select(t => t.OverallSummary));
        }

        return "새로운 대화 시작";
    }
}

/// <summary>
/// 개별 턴(대화 라운드)의 요약 정보
/// </summary>
public class TurnSummary
{
    public int TurnNumber { get; }
    public string UserInput { get; }
    public string RefinedInput { get; }
    public SystemCapabilityType SelectedCapability { get; }
    public List<ToolExecution> ToolExecutions { get; }
    public string FinalResponse { get; }
    public string OverallSummary { get; }
    public DateTime CompletedAt { get; }

    public TurnSummary(
        int turnNumber,
        string userInput,
        string refinedInput,
        SystemCapabilityType selectedCapability,
        List<ToolExecution>? toolExecutions,
        string finalResponse,
        string overallSummary)
    {
        TurnNumber = turnNumber;
        UserInput = userInput ?? throw new ArgumentNullException(nameof(userInput));
        RefinedInput = refinedInput ?? throw new ArgumentNullException(nameof(refinedInput));
        SelectedCapability = selectedCapability;
        ToolExecutions = toolExecutions ?? new List<ToolExecution>();
        FinalResponse = finalResponse ?? throw new ArgumentNullException(nameof(finalResponse));
        OverallSummary = overallSummary ?? throw new ArgumentNullException(nameof(overallSummary));
        CompletedAt = DateTime.UtcNow;
    }
}

