using McpAgent.Domain.Entities;

namespace McpAgent.Application.Interfaces;

/// <summary>
/// 입력 분석과 기능 선택을 동시에 수행하는 통합 서비스 인터페이스
/// </summary>
public interface IIntegratedAnalysisService
{
    /// <summary>
    /// 입력 분석과 기능 선택을 동시에 수행
    /// 즉시 응답 가능한 경우 응답 내용도 함께 생성하여 LLM 호출 횟수를 최적화
    /// </summary>
    /// <param name="originalInput">원본 사용자 입력</param>
    /// <param name="conversationHistory">대화 이력</param>
    /// <param name="systemContext">시스템 컨텍스트</param>
    /// <param name="toolExecutionResults">이전 도구 실행 결과 (있는 경우)</param>
    /// <param name="cumulativePlans">누적된 계획 목록 (있는 경우)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>통합 분석 결과</returns>
    Task<IntegratedAnalysisResult> AnalyzeAndSelectAsync(
        string originalInput,
        IReadOnlyList<ConversationMessage> conversationHistory,
        string systemContext,
        IReadOnlyList<ToolExecution>? toolExecutionResults = null,
        List<string>? cumulativePlans = null,
        CancellationToken cancellationToken = default);
}