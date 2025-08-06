using McpAgent.Models;
using McpAgent.Services;
using Microsoft.Extensions.Logging;

namespace McpAgent.Core;

public class ConversationOrchestrator
{
    private readonly ILogger<ConversationOrchestrator> _logger;
    private readonly IPhaseExecutorFactory _phaseExecutorFactory;
    private readonly IConversationStateManager _stateManager;
    private readonly ErrorRecoveryManager _errorRecovery;
    
    public ConversationOrchestrator(
        ILogger<ConversationOrchestrator> logger,
        IPhaseExecutorFactory phaseExecutorFactory,
        IConversationStateManager stateManager,
        ErrorRecoveryManager errorRecovery)
    {
        _logger = logger;
        _phaseExecutorFactory = phaseExecutorFactory;
        _stateManager = stateManager;
        _errorRecovery = errorRecovery;
    }
    
    public async Task<AgentResponse> ProcessAsync(string userInput, string conversationId)
    {
        var state = await _stateManager.GetStateAsync(conversationId);
        var maxIterations = 20; // 전체 대화 최대 반복
        var iteration = 0;
        
        // 사용자 컨텍스트 업데이트
        UpdateUserContext(state, userInput);
        
        while (iteration < maxIterations)
        {
            iteration++;
            
            try
            {
                var phaseResult = await ExecutePhaseAsync(state.CurrentPhase, state, userInput);
                state.PhaseHistory[state.CurrentPhase] = phaseResult;
                
                // 루프 히스토리 업데이트
                UpdateLoopHistory(state, phaseResult);
                
                // 성공적인 완료 체크
                if (state.CurrentPhase == 5 && !state.ShouldLoop(5, phaseResult))
                {
                    await _stateManager.SaveStateAsync(state);
                    return CreateFinalResponse(phaseResult, state);
                }
                
                // 다음 단계 결정
                var nextPhase = state.GetNextPhase(state.CurrentPhase, phaseResult);
                
                // 사용자 입력이 필요한 경우
                if (phaseResult.RequiresUserInput)
                {
                    await _stateManager.SaveStateAsync(state);
                    return CreateInterimResponse(phaseResult, nextPhase, state);
                }
                
                state.CurrentPhase = nextPhase;
                
                // 무한 루프 방지
                if (await _stateManager.IsStuckInLoopAsync(state))
                {
                    return CreateErrorResponse("대화가 복잡해져서 처리할 수 없습니다. 다시 시도해 주세요.", state);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in phase {Phase} for conversation {ConversationId}", 
                    state.CurrentPhase, conversationId);
                    
                try
                {
                    var recovery = await _errorRecovery.RecoverFromErrorAsync(ex, state);
                    if (recovery != null)
                    {
                        return recovery;
                    }
                }
                catch (Exception recoveryEx)
                {
                    _logger.LogError(recoveryEx, "Error recovery failed for conversation {ConversationId}", 
                        conversationId);
                }
                
                return CreateErrorResponse($"처리 중 오류가 발생했습니다: {ex.Message}", state);
            }
        }
        
        return CreateErrorResponse("처리 시간이 너무 오래 걸립니다.", state);
    }
    
    private async Task<PhaseResult> ExecutePhaseAsync(int phase, ConversationState state, string userInput)
    {
        var executor = _phaseExecutorFactory.GetExecutor(phase);
        
        _logger.LogDebug("Executing phase {Phase} for conversation {ConversationId}", 
            phase, state.ConversationId);
            
        var startTime = DateTime.UtcNow;
        
        try
        {
            // Add timeout per phase (60 seconds)
            using var phaseCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var result = await executor.ExecuteAsync(state, userInput, phaseCts.Token);
            var duration = DateTime.UtcNow - startTime;
            
            _logger.LogDebug("Phase {Phase} completed in {Duration}ms with status {Status}", 
                phase, duration.TotalMilliseconds, result.Status);
                
            return result;
        }
        catch (OperationCanceledException ex)
        {
            var duration = DateTime.UtcNow - startTime;
            _logger.LogError("Phase {Phase} timed out after {Duration}ms", phase, duration.TotalMilliseconds);
            
            return new PhaseResult
            {
                Phase = phase,
                Status = ExecutionStatus.Failure,
                ErrorMessage = "Phase execution timed out",
                Data = new Dictionary<string, object> 
                { 
                    ["timeout"] = true,
                    ["duration_ms"] = duration.TotalMilliseconds
                }
            };
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - startTime;
            _logger.LogError(ex, "Phase {Phase} failed after {Duration}ms", phase, duration.TotalMilliseconds);
            
            return new PhaseResult
            {
                Phase = phase,
                Status = ExecutionStatus.Failure,
                ErrorMessage = ex.Message,
                Data = new Dictionary<string, object> 
                { 
                    ["error_type"] = ex.GetType().Name,
                    ["duration_ms"] = duration.TotalMilliseconds
                }
            };
        }
    }
    
    private void UpdateUserContext(ConversationState state, string userInput)
    {
        state.UserContext.LastUserInput = userInput;
        state.UserContext.RecentQueries.Add(userInput);
        
        // 최근 쿼리는 최대 10개만 유지
        if (state.UserContext.RecentQueries.Count > 10)
        {
            state.UserContext.RecentQueries.RemoveAt(0);
        }
    }
    
    private void UpdateLoopHistory(ConversationState state, PhaseResult result)
    {
        var currentPhase = state.CurrentPhase;
        var nextPhase = state.GetNextPhase(currentPhase, result);
        
        if (currentPhase != nextPhase)
        {
            var decision = new LoopDecision
            {
                FromPhase = currentPhase,
                ToPhase = nextPhase,
                Reason = result.Status.ToString()
            };
            
            state.LoopContext.LoopHistory.Add(decision);
            
            // 루프 카운트 업데이트
            if (nextPhase <= currentPhase) // 루프 감지
            {
                state.LoopContext.PhaseLoopCounts[currentPhase] = 
                    state.LoopContext.PhaseLoopCounts.GetValueOrDefault(currentPhase, 0) + 1;
            }
        }
    }
    
    private AgentResponse CreateFinalResponse(PhaseResult phaseResult, ConversationState state)
    {
        var message = phaseResult.Data.GetValueOrDefault("natural_response")?.ToString() ?? 
                     "작업이 완료되었습니다.";
                     
        var followUpSuggestions = phaseResult.Data.GetValueOrDefault("follow_up_suggestions") as List<string> ?? new();
        
        return new AgentResponse
        {
            ConversationId = state.ConversationId,
            Message = message,
            IsSuccess = true,
            Metadata = new Dictionary<string, object>
            {
                ["phase_history"] = state.PhaseHistory.Keys.ToList(),
                ["total_phases"] = state.PhaseHistory.Count,
                ["follow_up_suggestions"] = followUpSuggestions,
                ["summary"] = phaseResult.Data.GetValueOrDefault("summary") ?? ""
            }
        };
    }
    
    private AgentResponse CreateInterimResponse(PhaseResult phaseResult, int nextPhase, ConversationState state)
    {
        var messages = phaseResult.Messages.Any() ? 
            string.Join("\n", phaseResult.Messages) : 
            "추가 정보가 필요합니다.";
            
        return new AgentResponse
        {
            ConversationId = state.ConversationId,
            Message = messages,
            IsSuccess = true,
            Metadata = new Dictionary<string, object>
            {
                ["requires_user_input"] = true,
                ["next_phase"] = nextPhase,
                ["current_phase"] = state.CurrentPhase,
                ["waiting_for"] = "user_input"
            }
        };
    }
    
    private AgentResponse CreateErrorResponse(string message, ConversationState state)
    {
        return new AgentResponse
        {
            ConversationId = state.ConversationId,
            Message = message,
            IsSuccess = false,
            Error = message,
            Metadata = new Dictionary<string, object>
            {
                ["failed_at_phase"] = state.CurrentPhase,
                ["phase_history"] = state.PhaseHistory.Keys.ToList()
            }
        };
    }
}