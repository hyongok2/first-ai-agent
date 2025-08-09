using McpAgent.Domain.Entities;

namespace McpAgent.Domain.Interfaces;

/// <summary>
/// 시스템 능력 선택을 담당하는 서비스
/// </summary>
public interface ICapabilitySelectionService
{
    /// <summary>
    /// 정제된 입력과 컨텍스트를 바탕으로 적절한 시스템 능력을 선택합니다.
    /// </summary>
    /// <param name="refinedInput">정제된 사용자 입력</param>
    /// <param name="conversationHistory">대화 이력</param>
    /// <param name="systemContext">시스템 기본 정보</param>
    /// <param name="toolExecutionResults">이전 도구 실행 결과 (있는 경우)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>선택된 시스템 능력</returns>
    Task<SystemCapability> SelectCapabilityAsync(
        RefinedInput refinedInput,
        IReadOnlyList<ConversationMessage> conversationHistory,
        string systemContext,
        IReadOnlyList<ToolExecution>? toolExecutionResults = null,
        CancellationToken cancellationToken = default);

}