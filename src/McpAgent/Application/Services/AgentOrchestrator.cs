using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;
using McpAgent.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace McpAgent.Application.Services;

public class AgentOrchestrator
{
    private readonly IInputRefinementService _inputRefinementService;
    private readonly ICapabilitySelectionService _capabilitySelectionService;
    private readonly IConversationSummaryService _conversationSummaryService;
    private readonly IParameterGenerationService _parameterGenerationService;
    private readonly IResponseGenerationService _responseGenerationService;
    private readonly IMcpClientAdapter _mcpClient;
    private readonly ILogger<AgentOrchestrator> _logger;

    // System context that gets passed to all services
    private const string DefaultSystemContext = @"
## 시스템 정보
- 이름: McpAgent
- 버전: 1.0
- 역할: AI 어시스턴트 with MCP 도구 지원
- 기능: 사용자 요청 처리, MCP 도구 실행, 대화 관리

## 처리 능력
- 의도 명확화 및 입력 정제
- 다양한 MCP 도구 활용
- 대화 이력 관리 및 요약
- 복잡한 작업 계획 수립
- 오류 처리 및 복구

## 응답 원칙
- 정확하고 유용한 정보 제공
- 사용자 친화적인 언어 사용
- 필요시 추가 정보 요청
- 안전하고 윤리적인 응답
    ";

    public AgentOrchestrator(
        IInputRefinementService inputRefinementService,
        ICapabilitySelectionService capabilitySelectionService,
        IConversationSummaryService conversationSummaryService,
        IParameterGenerationService parameterGenerationService,
        IResponseGenerationService responseGenerationService,
        IMcpClientAdapter mcpClient,
        ILogger<AgentOrchestrator> logger)
    {
        _inputRefinementService = inputRefinementService;
        _capabilitySelectionService = capabilitySelectionService;
        _conversationSummaryService = conversationSummaryService;
        _parameterGenerationService = parameterGenerationService;
        _responseGenerationService = responseGenerationService;
        _mcpClient = mcpClient;
        _logger = logger;
    }

    public async Task<AgentResponse> ProcessRequestAsync(AgentRequest request, CancellationToken cancellationToken = default)
    {
        // Track turn number - in a real system this should be persisted
        var turnNumber = await GetNextTurnNumberAsync(request.ConversationId, cancellationToken);
        
        var result = await ProcessUserInputAsync(request.Message, request.ConversationId, turnNumber, cancellationToken);
        
        if (result.Success)
        {
            return AgentResponse.Success(result.FinalResponse, request.ConversationId);
        }
        else
        {
            return AgentResponse.Failure(result.Error ?? "Processing failed", request.ConversationId);
        }
    }

    public async Task<ProcessingResult> ProcessUserInputAsync(
        string userInput,
        string conversationId,
        int turnNumber,
        CancellationToken cancellationToken = default)
    {
        var processingContext = new ProcessingContext
        {
            ConversationId = conversationId,
            TurnNumber = turnNumber,
            OriginalInput = userInput,
            CurrentPhase = ProcessingPhase.InputRefinement,
            StartedAt = DateTime.UtcNow
        };

        try
        {
            _logger.LogInformation("Starting multi-step processing for turn {Turn} in conversation {ConversationId}", 
                turnNumber, conversationId);

            // Phase 1: Input Refinement
            var refinedInput = await ExecuteInputRefinementPhaseAsync(processingContext, cancellationToken);
            processingContext.RefinedInput = refinedInput;
            processingContext.CurrentPhase = ProcessingPhase.CapabilitySelection;

            // Phase 2: Capability Selection
            var selectedCapability = await ExecuteCapabilitySelectionPhaseAsync(processingContext, cancellationToken);
            processingContext.SelectedCapability = selectedCapability;

            // Phase 3: Tool Execution (if needed)
            string toolExecutionResults = "";
            if (selectedCapability.Type == SystemCapabilityType.McpTool)
            {
                processingContext.CurrentPhase = ProcessingPhase.ParameterGeneration;
                toolExecutionResults = await ExecuteToolPhaseAsync(processingContext, cancellationToken);
                processingContext.ToolExecutionResults = toolExecutionResults;
            }

            // Phase 4: Response Generation
            processingContext.CurrentPhase = ProcessingPhase.ResponseGeneration;
            var finalResponse = await ExecuteResponseGenerationPhaseAsync(processingContext, cancellationToken);
            processingContext.FinalResponse = finalResponse;

            // Phase 5: Conversation Summary
            processingContext.CurrentPhase = ProcessingPhase.ConversationSummary;
            await ExecuteConversationSummaryPhaseAsync(processingContext, cancellationToken);

            processingContext.CurrentPhase = ProcessingPhase.Completed;
            processingContext.CompletedAt = DateTime.UtcNow;

            _logger.LogInformation("Multi-step processing completed successfully for turn {Turn}", turnNumber);

            return new ProcessingResult
            {
                Success = true,
                FinalResponse = finalResponse,
                ProcessingContext = processingContext,
                ExecutionTimeMs = (int)(processingContext.CompletedAt!.Value - processingContext.StartedAt).TotalMilliseconds
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process user input for turn {Turn} in conversation {ConversationId}", 
                turnNumber, conversationId);

            return new ProcessingResult
            {
                Success = false,
                FinalResponse = "죄송합니다. 요청을 처리하는 중 문제가 발생했습니다. 다시 시도해 주세요.",
                Error = ex.Message,
                ProcessingContext = processingContext
            };
        }
    }

    private async Task<RefinedInput> ExecuteInputRefinementPhaseAsync(
        ProcessingContext context,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing input refinement phase for turn {Turn}", context.TurnNumber);

        // Get conversation history for context
        var conversationHistoryString = await _conversationSummaryService
            .GetConversationHistoryAsync(context.ConversationId, cancellationToken);
        
        // Convert string history to conversation messages (simplified for now)
        var conversationHistory = new List<ConversationMessage>();
        if (!string.IsNullOrEmpty(conversationHistoryString) && conversationHistoryString != "새로운 대화 시작")
        {
            conversationHistory.Add(new ConversationMessage(MessageRole.System, conversationHistoryString));
        }

        // Refine the user input
        var refinedInput = await _inputRefinementService.RefineInputAsync(
            context.OriginalInput,
            conversationHistory.AsReadOnly(),
            DefaultSystemContext,
            cancellationToken);

        _logger.LogInformation("Input refinement completed. Confidence: {Confidence:F2}", 
            refinedInput.ConfidenceScore);

        return refinedInput;
    }

    private async Task<SystemCapability> ExecuteCapabilitySelectionPhaseAsync(
        ProcessingContext context,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing capability selection phase for turn {Turn}", context.TurnNumber);

        // Get conversation history for context
        var conversationHistoryString = await _conversationSummaryService
            .GetConversationHistoryAsync(context.ConversationId, cancellationToken);
        
        // Convert string history to conversation messages (simplified for now)
        var conversationHistory = new List<ConversationMessage>();
        if (!string.IsNullOrEmpty(conversationHistoryString) && conversationHistoryString != "새로운 대화 시작")
        {
            conversationHistory.Add(new ConversationMessage(MessageRole.System, conversationHistoryString));
        }

        // Get available capabilities
        var availableCapabilities = await _capabilitySelectionService.GetAvailableCapabilitiesAsync();
        
        // Select the most appropriate capability
        var selectedCapability = await _capabilitySelectionService.SelectCapabilityAsync(
            context.RefinedInput!,
            conversationHistory.AsReadOnly(),
            DefaultSystemContext,
            availableCapabilities,
            new List<ToolExecution>().AsReadOnly(), // Empty tool executions for now
            cancellationToken);

        _logger.LogInformation("Capability selected: {Capability}", selectedCapability.Type);

        return selectedCapability;
    }

    private async Task<string> ExecuteToolPhaseAsync(
        ProcessingContext context,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing tool phase for turn {Turn}", context.TurnNumber);

        // Check if tool name is specified in capability parameters
        if (!context.SelectedCapability!.Parameters.TryGetValue("tool_name", out var toolNameObj) ||
            toolNameObj is not string toolName || string.IsNullOrEmpty(toolName))
        {
            _logger.LogWarning("No tool name specified in capability parameters, using default tool");
            toolName = "echo"; // Fallback to echo tool
        }

        // Get available tools from MCP client
        var availableTools = await _mcpClient.GetAvailableToolsAsync(cancellationToken);
        var selectedTool = availableTools.FirstOrDefault(t => t.Name == toolName);

        if (selectedTool == null)
        {
            _logger.LogWarning("Tool {Tool} not found, using fallback", toolName);
            return $"도구 '{toolName}'을 찾을 수 없습니다.";
        }

        // Get conversation history for parameter generation
        var conversationHistoryString = await _conversationSummaryService.GetConversationHistoryAsync(context.ConversationId, cancellationToken);
        var conversationHistory = new List<ConversationMessage>();
        if (!string.IsNullOrEmpty(conversationHistoryString) && conversationHistoryString != "새로운 대화 시작")
        {
            conversationHistory.Add(new ConversationMessage(MessageRole.System, conversationHistoryString));
        }
        
        // Generate parameters for the tool
        var parameters = await _parameterGenerationService.GenerateParametersAsync(
            selectedTool.Name,
            selectedTool,
            context.RefinedInput!,
            conversationHistory.AsReadOnly(),
            DefaultSystemContext,
            cancellationToken);

        // Execute the tool
        try
        {
            var toolResult = await _mcpClient.CallToolAsync(
                selectedTool.Name,
                parameters,
                cancellationToken);

            var resultString = toolResult?.ToString() ?? "도구 실행 결과를 받지 못했습니다.";
            
            _logger.LogInformation("Tool {Tool} executed successfully", selectedTool.Name);
            
            return resultString;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute tool {Tool}", selectedTool.Name);
            return $"도구 '{selectedTool.Name}' 실행 중 오류가 발생했습니다: {ex.Message}";
        }
    }

    private async Task<string> ExecuteResponseGenerationPhaseAsync(
        ProcessingContext context,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing response generation phase for turn {Turn}", context.TurnNumber);

        // Get conversation history for context
        var conversationHistoryString = await _conversationSummaryService
            .GetConversationHistoryAsync(context.ConversationId, cancellationToken);
        
        // Convert string history to conversation messages (simplified for now)
        var conversationHistory = new List<ConversationMessage>();
        if (!string.IsNullOrEmpty(conversationHistoryString) && conversationHistoryString != "새로운 대화 시작")
        {
            conversationHistory.Add(new ConversationMessage(MessageRole.System, conversationHistoryString));
        }

        // Generate the final response  
        var response = await _responseGenerationService.GenerateResponseAsync(
            context.RefinedInput!,
            context.SelectedCapability!,
            conversationHistory.AsReadOnly(),
            new List<ToolExecution>().AsReadOnly(), // Empty tool executions for now
            DefaultSystemContext,
            cancellationToken);

        _logger.LogInformation("Response generated successfully");

        return response;
    }

    private async Task ExecuteConversationSummaryPhaseAsync(
        ProcessingContext context,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing conversation summary phase for turn {Turn}", context.TurnNumber);

        try
        {
            // Summarize this turn
            await _conversationSummaryService.SummarizeTurnAsync(
                context.TurnNumber,
                context.OriginalInput,
                context.RefinedInput!,
                context.SelectedCapability!,
                new List<ToolExecution>().AsReadOnly(), // Empty tool executions for now
                context.FinalResponse ?? "",
                DefaultSystemContext,
                cancellationToken);

            _logger.LogInformation("Conversation turn summarized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to summarize conversation turn {Turn}", context.TurnNumber);
            // Don't fail the entire process if summarization fails
        }
    }

    private async Task<int> GetNextTurnNumberAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        // Get current conversation summary to determine turn number
        var conversationSummary = await _conversationSummaryService.GetConversationSummaryAsync(conversationId, cancellationToken);
        return conversationSummary.TotalTurns + 1;
    }
}

// Result object for the orchestrator
public class ProcessingResult
{
    public bool Success { get; set; }
    public string FinalResponse { get; set; } = "";
    public string? Error { get; set; }
    public ProcessingContext ProcessingContext { get; set; } = new();
    public int ExecutionTimeMs { get; set; }
}