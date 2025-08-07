using McpAgent.Domain.Entities;

namespace McpAgent.Domain.Interfaces;

/// <summary>
/// 최종 사용자 응답 생성을 담당하는 서비스
/// </summary>
public interface IResponseGenerationService
{
    /// <summary>
    /// 컨텍스트 정보를 바탕으로 최종 사용자 응답을 생성합니다.
    /// </summary>
    /// <param name="refinedInput">정제된 사용자 입력</param>
    /// <param name="selectedCapability">선택된 시스템 능력</param>
    /// <param name="conversationHistory">대화 이력</param>
    /// <param name="toolExecutionResults">도구 실행 결과 (있는 경우)</param>
    /// <param name="systemContext">시스템 컨텍스트</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>생성된 응답</returns>
    Task<string> GenerateResponseAsync(
        RefinedInput refinedInput,
        SystemCapability selectedCapability,
        IReadOnlyList<ConversationMessage> conversationHistory,
        IReadOnlyList<ToolExecution>? toolExecutionResults = null,
        string? systemContext = null,
        CancellationToken cancellationToken = default);
}