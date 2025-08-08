using System.Collections.Generic;

namespace McpAgent.Domain.Entities;

/// <summary>
/// LLM 요약 기반의 메모리 효율적인 대화 관리
/// </summary>
public class SummarizedConversation
{
    public string Id { get; }
    public DateTime CreatedAt { get; }
    public DateTime LastActivity { get; private set; }
    public ConversationStatus Status { get; private set; }
    
    /// <summary>
    /// 현재 진행 중인 턴의 메시지들 (요약 전)
    /// </summary>
    public List<ConversationMessage> CurrentTurnMessages { get; }
    
    /// <summary>
    /// 요약된 이전 대화 이력
    /// </summary>
    public ConversationSummary Summary { get; private set; }
    
    /// <summary>
    /// 현재 턴 번호
    /// </summary>
    public int CurrentTurnNumber { get; private set; }

    public SummarizedConversation(string id)
    {
        Id = id;
        CreatedAt = DateTime.UtcNow;
        LastActivity = DateTime.UtcNow;
        Status = ConversationStatus.Active;
        CurrentTurnMessages = new List<ConversationMessage>();
        Summary = new ConversationSummary(id);
        CurrentTurnNumber = 1;
    }

    /// <summary>
    /// 현재 턴에 메시지 추가
    /// </summary>
    public void AddCurrentTurnMessage(ConversationMessage message)
    {
        CurrentTurnMessages.Add(message);
        LastActivity = DateTime.UtcNow;
    }

    /// <summary>
    /// 현재 턴을 요약하여 완료하고 새로운 턴 시작
    /// </summary>
    public void CompleteTurnWithSummary(TurnSummary turnSummary)
    {
        // 요약을 추가
        Summary.AddTurnSummary(turnSummary);
        
        // 현재 턴 메시지 클리어 (메모리 절약)
        CurrentTurnMessages.Clear();
        
        // 다음 턴으로 진행
        CurrentTurnNumber++;
        LastActivity = DateTime.UtcNow;
    }

    /// <summary>
    /// 통합된 대화 컨텍스트 가져오기 (요약 + 현재 턴)
    /// </summary>
    public string GetFullConversationContext()
    {
        var context = new List<string>();
        
        // 이전 요약 컨텍스트
        if (Summary.TotalTurns > 0)
        {
            if (!string.IsNullOrEmpty(Summary.ConsolidatedSummary))
            {
                context.Add($"=== 이전 대화 통합 요약 ({Summary.TotalTurns - Summary.IndividualTurns.Count}턴) ===");
                context.Add(Summary.ConsolidatedSummary);
            }
            
            if (Summary.IndividualTurns.Any())
            {
                context.Add($"=== 최근 개별 턴 요약 ({Summary.IndividualTurns.Count}턴) ===");
                foreach (var turn in Summary.IndividualTurns.OrderBy(t => t.TurnNumber))
                {
                    context.Add($"턴 {turn.TurnNumber}: {turn.OverallSummary}");
                }
            }
        }
        
        // 현재 턴 메시지들
        if (CurrentTurnMessages.Any())
        {
            context.Add($"=== 현재 턴 {CurrentTurnNumber} 진행 중 ===");
            foreach (var msg in CurrentTurnMessages)
            {
                context.Add($"{msg.Role}: {msg.Content}");
            }
        }
        
        return context.Any() ? string.Join("\n\n", context) : "새로운 대화입니다.";
    }
    
    /// <summary>
    /// 현재 턴의 메시지들을 ConversationMessage 리스트로 반환 (기존 호환성)
    /// </summary>
    public IReadOnlyList<ConversationMessage> GetMessages() => CurrentTurnMessages.AsReadOnly();
    
    /// <summary>
    /// 마지막 메시지 가져오기 (기존 호환성)
    /// </summary>
    public ConversationMessage? GetLastMessage() => CurrentTurnMessages.LastOrDefault();

    public void SetStatus(ConversationStatus status)
    {
        Status = status;
        LastActivity = DateTime.UtcNow;
    }
    
    /// <summary>
    /// 통합 요약을 업데이트 (5턴마다 호출)
    /// </summary>
    public void UpdateConsolidatedSummary(string consolidatedSummary)
    {
        Summary.SetConsolidatedSummary(consolidatedSummary);
        LastActivity = DateTime.UtcNow;
    }
}