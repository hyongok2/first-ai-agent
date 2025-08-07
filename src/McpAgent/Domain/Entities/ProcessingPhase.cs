namespace McpAgent.Domain.Entities;

/// <summary>
/// 에이전트 처리 단계를 정의합니다.
/// </summary>
public enum ProcessingPhase
{
    /// <summary>입력 정제 및 정리 단계</summary>
    InputRefinement,
    
    /// <summary>시스템 능력 선택 단계</summary>
    CapabilitySelection,
    
    /// <summary>응답 생성 단계</summary>
    ResponseGeneration,
    
    /// <summary>도구 파라미터 생성 단계</summary>
    ParameterGeneration,
    
    /// <summary>도구 실행 단계</summary>
    ToolExecution,
    
    /// <summary>결과 평가 단계</summary>
    ResultEvaluation,
    
    /// <summary>대화 요약 단계</summary>
    ConversationSummary,
    
    /// <summary>완료 단계</summary>
    Completed
}