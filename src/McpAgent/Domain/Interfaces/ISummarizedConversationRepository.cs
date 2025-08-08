using McpAgent.Domain.Entities;

namespace McpAgent.Domain.Interfaces;

/// <summary>
/// LLM 요약 기반의 메모리 효율적인 대화 저장소
/// </summary>
public interface ISummarizedConversationRepository
{
    /// <summary>
    /// 대화 조회
    /// </summary>
    Task<SummarizedConversation?> GetByIdAsync(string conversationId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 새 대화 생성
    /// </summary>
    Task<SummarizedConversation> CreateAsync(string conversationId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 현재 턴에 메시지 추가
    /// </summary>
    Task AddMessageToCurrentTurnAsync(string conversationId, ConversationMessage message, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 현재 턴을 요약으로 완료하고 저장
    /// </summary>
    Task CompleteTurnWithSummaryAsync(string conversationId, TurnSummary turnSummary, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 통합 요약 업데이트 (5턴마다)
    /// </summary>
    Task UpdateConsolidatedSummaryAsync(string conversationId, string consolidatedSummary, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 전체 대화 컨텍스트 가져오기 (요약 + 현재 진행)
    /// </summary>
    Task<string> GetFullConversationContextAsync(string conversationId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 활성 대화 ID 목록
    /// </summary>
    Task<IReadOnlyList<string>> GetActiveConversationIdsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 대화 삭제
    /// </summary>
    Task DeleteAsync(string conversationId, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 대화 상태 변경
    /// </summary>
    Task SetStatusAsync(string conversationId, ConversationStatus status, CancellationToken cancellationToken = default);
}