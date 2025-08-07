using McpAgent.Domain.Entities;

namespace McpAgent.Domain.Interfaces;

/// <summary>
/// 사용자 입력을 정제하고 정리하는 서비스
/// </summary>
public interface IInputRefinementService
{
    /// <summary>
    /// 사용자 입력과 대화 이력을 바탕으로 의도를 명확히 하고 정제된 입력을 생성합니다.
    /// </summary>
    /// <param name="originalInput">원본 사용자 입력</param>
    /// <param name="conversationHistory">대화 이력</param>
    /// <param name="systemContext">시스템 기본 정보</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>정제된 입력 정보</returns>
    Task<RefinedInput> RefineInputAsync(
        string originalInput,
        IReadOnlyList<ConversationMessage> conversationHistory,
        string systemContext,
        CancellationToken cancellationToken = default);
}