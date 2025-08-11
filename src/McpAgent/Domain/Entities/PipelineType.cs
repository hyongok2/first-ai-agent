namespace McpAgent.Domain.Entities;

/// <summary>
/// 5단계 파이프라인의 각 단계를 나타내는 열거형
/// </summary>
public enum PipelineType
{
    /// <summary>
    /// 입력 정제 단계 - 사용자 입력을 분석하고 정제
    /// </summary>
    InputRefinement,

    /// <summary>
    /// 능력 선택 단계 - 적절한 시스템 능력 선택
    /// </summary>
    CapabilitySelection,

    /// <summary>
    /// 파라미터 생성 단계 - 도구 실행을 위한 파라미터 생성
    /// </summary>
    ParameterGeneration,

    /// <summary>
    /// 응답 생성 단계 - 최종 사용자 응답 생성
    /// </summary>
    ResponseGeneration,

    /// <summary>
    /// 대화 요약 단계 - 대화 내용 요약
    /// </summary>
    ConversationSummary,

    /// <summary>
    /// 통합 분석 단계 - 입력 분석과 능력 선택을 동시 수행 (최적화 파이프라인)
    /// </summary>
    IntegratedAnalysis
}