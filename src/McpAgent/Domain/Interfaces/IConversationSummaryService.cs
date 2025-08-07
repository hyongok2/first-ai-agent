using McpAgent.Domain.Entities;

namespace McpAgent.Domain.Interfaces;

/// <summary>
/// 대화 이력 요약을 담당하는 서비스
/// </summary>
public interface IConversationSummaryService
{
    /// <summary>
    /// 현재 턴의 모든 정보를 요약하여 TurnSummary를 생성합니다.
    /// </summary>
    /// <param name="turnNumber">턴 번호</param>
    /// <param name="originalInput">원본 사용자 입력</param>
    /// <param name="refinedInput">정제된 입력</param>
    /// <param name="selectedCapability">선택된 능력</param>
    /// <param name="toolExecutions">실행된 도구들</param>
    /// <param name="finalResponse">최종 응답</param>
    /// <param name="systemContext">시스템 컨텍스트</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>턴 요약</returns>
    Task<TurnSummary> SummarizeTurnAsync(
        int turnNumber,
        string originalInput,
        RefinedInput refinedInput,
        SystemCapability selectedCapability,
        IReadOnlyList<ToolExecution> toolExecutions,
        string finalResponse,
        string systemContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 여러 개의 개별 턴 요약을 하나의 통합 요약으로 만듭니다.
    /// </summary>
    /// <param name="individualTurns">개별 턴 요약들</param>
    /// <param name="systemContext">시스템 컨텍스트</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>통합 요약</returns>
    Task<string> ConsolidateTurnsAsync(
        IReadOnlyList<TurnSummary> individualTurns,
        string systemContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 대화 요약 정보를 가져옵니다.
    /// </summary>
    Task<ConversationSummary> GetConversationSummaryAsync(
        string conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 대화 이력을 문자열로 형태로 가져옵니다.
    /// </summary>
    Task<string> GetConversationHistoryAsync(
        string conversationId,
        CancellationToken cancellationToken = default);
}