namespace McpAgent.Domain.Entities;

/// <summary>
/// 시스템이 수행할 수 있는 능력 유형을 정의합니다.
/// </summary>
public enum SystemCapabilityType
{
    /// <summary>사용자 의도가 불분명하여 명확화가 필요한 경우</summary>
    IntentClarification,
    
    /// <summary>일반적인 대화 응답 (도구 사용 없음)</summary>
    SimpleChat,
    
    /// <summary>사용자 요청이 완료되어 최종 응답하는 경우</summary>
    TaskCompletion,
    
    /// <summary>내부 시스템 도구 사용 (추후 확장)</summary>
    InternalTool,
    
    /// <summary>MCP 외부 도구 사용</summary>
    McpTool,
    
    /// <summary>복잡한 작업을 위한 계획 수립</summary>
    TaskPlanning,
    
    /// <summary>오류 또는 예외 상황 처리</summary>
    ErrorHandling
}

/// <summary>
/// 선택된 시스템 능력 정보
/// </summary>
public class SystemCapability
{
    public SystemCapabilityType Type { get; }
    public string Description { get; }
    public string Reasoning { get; }
    public Dictionary<string, object> Parameters { get; }
    public bool RequiresToolExecution { get; }
    public bool EndsConversation { get; }

    public SystemCapability(
        SystemCapabilityType type,
        string description,
        string reasoning,
        Dictionary<string, object>? parameters = null)
    {
        Type = type;
        Description = description ?? throw new ArgumentNullException(nameof(description));
        Reasoning = reasoning ?? throw new ArgumentNullException(nameof(reasoning));
        Parameters = parameters ?? new Dictionary<string, object>();
        RequiresToolExecution = DetermineRequiresToolExecution(type);
        EndsConversation = DetermineEndsConversation(type);
    }

    private static bool DetermineRequiresToolExecution(SystemCapabilityType type)
    {
        return type switch
        {
            SystemCapabilityType.InternalTool => true,
            SystemCapabilityType.McpTool => true,
            _ => false
        };
    }

    private static bool DetermineEndsConversation(SystemCapabilityType type)
    {
        return type switch
        {
            SystemCapabilityType.IntentClarification => true,
            SystemCapabilityType.SimpleChat => true,
            SystemCapabilityType.TaskCompletion => true,
            SystemCapabilityType.ErrorHandling => true,
            _ => false
        };
    }
}