namespace McpAgent.Domain.Entities;

/// <summary>
/// 통합 분석 결과를 담는 엔티티
/// 입력 분석, 기능 선택, 그리고 즉시 응답 가능한 경우 응답 내용까지 포함
/// </summary>
public class IntegratedAnalysisResult
{
    /// <summary>
    /// 분석된 입력 정보
    /// </summary>
    public RefinedInput RefinedInput { get; }
    
    /// <summary>
    /// 선택된 시스템 기능
    /// </summary>
    public SystemCapability SelectedCapability { get; }
    
    /// <summary>
    /// 즉시 응답이 가능한지 여부
    /// true인 경우 추가 도구 실행 없이 DirectResponseMessage를 사용자에게 반환
    /// </summary>
    public bool HasDirectResponse { get; }
    
    /// <summary>
    /// 즉시 응답 메시지 (HasDirectResponse가 true인 경우에만 사용)
    /// </summary>
    public string DirectResponseMessage { get; }

    public IntegratedAnalysisResult(
        RefinedInput refinedInput,
        SystemCapability selectedCapability,
        bool hasDirectResponse,
        string directResponseMessage)
    {
        RefinedInput = refinedInput ?? throw new ArgumentNullException(nameof(refinedInput));
        SelectedCapability = selectedCapability ?? throw new ArgumentNullException(nameof(selectedCapability));
        HasDirectResponse = hasDirectResponse;
        DirectResponseMessage = directResponseMessage ?? string.Empty;
    }

    /// <summary>
    /// 즉시 응답이 가능하고 TaskCompletion 타입인지 확인
    /// </summary>
    public bool IsImmediateCompletion => HasDirectResponse && 
                                         SelectedCapability.Type == SystemCapabilityType.TaskCompletion;

    /// <summary>
    /// 도구 실행이 필요한지 확인
    /// </summary>
    public bool RequiresToolExecution => !HasDirectResponse && 
                                         SelectedCapability.Type == SystemCapabilityType.McpTool;

    /// <summary>
    /// 추가 LLM 호출이 필요한지 확인 (응답 생성용)
    /// </summary>
    public bool RequiresResponseGeneration => !HasDirectResponse;
}