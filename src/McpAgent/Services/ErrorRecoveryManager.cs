using System.Text.Json;
using McpAgent.Models;
using Microsoft.Extensions.Logging;

namespace McpAgent.Services;

public class ErrorRecoveryManager
{
    private readonly ILogger<ErrorRecoveryManager> _logger;
    
    public ErrorRecoveryManager(ILogger<ErrorRecoveryManager> logger)
    {
        _logger = logger;
    }
    
    public async Task<AgentResponse?> RecoverFromErrorAsync(Exception error, ConversationState state)
    {
        var recoveryAction = DetermineRecoveryAction(state.CurrentPhase, error, state);
        
        _logger.LogWarning("Attempting recovery with action {Action} for error: {Error}", 
            recoveryAction, error.Message);
            
        return recoveryAction switch
        {
            RecoveryAction.RetryWithShorterPrompt => await RetryWithReducedContext(state),
            RecoveryAction.FallbackToAlternativeTool => await FindAlternativeFunction(state),
            RecoveryAction.RequestMissingParameters => CreateParameterRequestResult(state),
            RecoveryAction.GracefulDegradation => CreateSimpleResponse("죄송합니다. 요청을 처리할 수 없습니다.", state),
            _ => CreateErrorResult("복구할 수 없는 오류가 발생했습니다.", state)
        };
    }
    
    public RecoveryAction DetermineRecoveryAction(int failedPhase, Exception error, ConversationState state)
    {
        return error switch
        {
            TimeoutException => RecoveryAction.RetryWithShorterPrompt,
            JsonException when failedPhase <= 3 => RecoveryAction.RetryWithStrictFormat,
            HttpRequestException => RecoveryAction.FallbackToAlternativeTool,
            ArgumentException when error.Message.Contains("parameter") => RecoveryAction.RequestMissingParameters,
            InvalidOperationException when error.Message.Contains("Tool") => RecoveryAction.FallbackToAlternativeTool,
            _ => RecoveryAction.GracefulDegradation
        };
    }
    
    private async Task<AgentResponse> RetryWithReducedContext(ConversationState state)
    {
        await Task.CompletedTask;
        
        return new AgentResponse
        {
            ConversationId = state.ConversationId,
            Message = "시스템이 일시적으로 바쁩니다. 좀 더 간단하게 질문해 주시겠어요?",
            IsSuccess = true,
            Metadata = new Dictionary<string, object>
            {
                ["recovery_action"] = "retry_with_reduced_context",
                ["suggested_action"] = "simplify_request"
            }
        };
    }
    
    private async Task<AgentResponse> FindAlternativeFunction(ConversationState state)
    {
        await Task.CompletedTask;
        
        return new AgentResponse
        {
            ConversationId = state.ConversationId,
            Message = "요청하신 기능을 사용할 수 없어 다른 방법을 찾고 있습니다. 잠시만 기다려 주세요.",
            IsSuccess = true,
            Metadata = new Dictionary<string, object>
            {
                ["recovery_action"] = "alternative_function",
                ["next_phase"] = 2 // 기능 선택 단계로 돌아가기
            }
        };
    }
    
    private AgentResponse CreateParameterRequestResult(ConversationState state)
    {
        return new AgentResponse
        {
            ConversationId = state.ConversationId,
            Message = "요청을 처리하기 위해 추가 정보가 필요합니다. 어떤 파일이나 정보를 원하시는지 구체적으로 알려주세요.",
            IsSuccess = true,
            Metadata = new Dictionary<string, object>
            {
                ["recovery_action"] = "request_parameters",
                ["requires_user_input"] = true,
                ["next_phase"] = 3 // 파라미터 생성 단계로 돌아가기
            }
        };
    }
    
    private AgentResponse CreateSimpleResponse(string message, ConversationState state)
    {
        return new AgentResponse
        {
            ConversationId = state.ConversationId,
            Message = message,
            IsSuccess = false,
            Metadata = new Dictionary<string, object>
            {
                ["recovery_action"] = "graceful_degradation",
                ["suggestion"] = "새로운 대화를 시작해 보세요."
            }
        };
    }
    
    private AgentResponse CreateErrorResult(string message, ConversationState state)
    {
        return new AgentResponse
        {
            ConversationId = state.ConversationId,
            Message = message,
            IsSuccess = false,
            Error = message,
            Metadata = new Dictionary<string, object>
            {
                ["recovery_action"] = "failed",
                ["suggestion"] = "시스템 관리자에게 문의하세요."
            }
        };
    }
}

public enum RecoveryAction
{
    RetryWithShorterPrompt,
    RetryWithStrictFormat,
    FallbackToAlternativeTool,
    RequestMissingParameters,
    GracefulDegradation,
    Failed
}