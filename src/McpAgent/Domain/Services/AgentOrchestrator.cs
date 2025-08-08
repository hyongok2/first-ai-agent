using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace McpAgent.Domain.Services;

/// <summary>
/// Multi-step pipeline orchestrator using dedicated prompt templates
/// </summary>
public class AgentOrchestrator
{
    private readonly ILogger<AgentOrchestrator> _logger;
    private readonly IInputRefinementService _inputRefinementService;
    private readonly ICapabilitySelectionService _capabilitySelectionService;
    private readonly IParameterGenerationService _parameterGenerationService;
    private readonly IResponseGenerationService _responseGenerationService;
    private readonly IConversationRepository _conversationRepository;
    private readonly ISummarizedConversationRepository _summarizedConversationRepository; // 나중에 사용할 예비용
    private readonly IToolExecutor _toolExecutor;
    private readonly IConversationSummaryService _conversationSummaryService; // 나중에 사용할 예비용

    public AgentOrchestrator(
        ILogger<AgentOrchestrator> logger,
        IInputRefinementService inputRefinementService,
        ICapabilitySelectionService capabilitySelectionService,
        IParameterGenerationService parameterGenerationService,
        IResponseGenerationService responseGenerationService,
        IConversationRepository conversationRepository,
        ISummarizedConversationRepository summarizedConversationRepository, // 나중에 사용할 예비용
        IToolExecutor toolExecutor,
        IConversationSummaryService conversationSummaryService) // 나중에 사용할 예비용
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _inputRefinementService = inputRefinementService ?? throw new ArgumentNullException(nameof(inputRefinementService));
        _capabilitySelectionService = capabilitySelectionService ?? throw new ArgumentNullException(nameof(capabilitySelectionService));
        _parameterGenerationService = parameterGenerationService ?? throw new ArgumentNullException(nameof(parameterGenerationService));
        _responseGenerationService = responseGenerationService ?? throw new ArgumentNullException(nameof(responseGenerationService));
        _conversationRepository = conversationRepository ?? throw new ArgumentNullException(nameof(conversationRepository));
        _summarizedConversationRepository = summarizedConversationRepository ?? throw new ArgumentNullException(nameof(summarizedConversationRepository));
        _toolExecutor = toolExecutor ?? throw new ArgumentNullException(nameof(toolExecutor));
        _conversationSummaryService = conversationSummaryService ?? throw new ArgumentNullException(nameof(conversationSummaryService));
    }

    public async Task<AgentResponse> ProcessRequestAsync(AgentRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting multi-cycle pipeline for conversation {ConversationId}", request.ConversationId);

            // Get or create conversation (기존 방식)
            var conversation = await _conversationRepository.GetByIdAsync(request.ConversationId, cancellationToken)
                ?? await _conversationRepository.CreateAsync(request.ConversationId, cancellationToken);

            // Add user message to conversation (기존 방식)
            var userMessage = new ConversationMessage(MessageRole.User, request.Message);
            conversation.AddMessage(userMessage);
            
            // 기존 대화 이력 사용 (전체 메시지 이력)
            var systemContext = $"AI 에이전트 - 대화ID: {request.ConversationId}";
            
            // TODO: 나중에 요약 기능 활성화 시 다음 코드 사용
            // var conversationContext = await _summarizedConversationRepository.GetFullConversationContextAsync(
            //     request.ConversationId, cancellationToken);
            // if (!string.IsNullOrEmpty(conversationContext) && conversationContext != "새로운 대화입니다.")
            // {
            //     systemContext += $"\n\n[대화 컨텍스트]\n{conversationContext}";
            // }
            var allToolExecutionResults = new List<ToolExecution>();
            var cycleCount = 0;
            const int maxCycles = 5; // 무한 루프 방지

            string currentInput = request.Message;
            string finalResponse = "";
            
            // 턴 번호 계산 (기존 방식)
            var turnNumber = conversation.GetMessages().Count(m => m.Role == MessageRole.User);
            
            // 초기 입력 정제 (처음에 한 번만 수행)
            RefinedInput? initialRefinedInput = null;
            SystemCapability? finalSelectedCapability = null;

            while (cycleCount < maxCycles)
            {
                cycleCount++;
                _logger.LogInformation("Starting cycle {CycleCount} for conversation {ConversationId}", cycleCount, request.ConversationId);

                // 각 사이클마다 최신 대화 이력 가져오기 (도구 실행 결과가 추가될 수 있으므로)
                var conversationHistory = conversation.GetMessages();

                // Step 1: Input Refinement
                _logger.LogInformation("Cycle {CycleCount} - Step 1: Input refinement", cycleCount);
                var refinedInput = await _inputRefinementService.RefineInputAsync(
                    currentInput, 
                    conversationHistory, 
                    systemContext, 
                    cancellationToken);
                    
                // 최초 사이클에서만 초기 정제 입력 저장
                if (cycleCount == 1)
                {
                    initialRefinedInput = refinedInput;
                }

                // Step 2: Capability Selection
                _logger.LogInformation("Cycle {CycleCount} - Step 2: Capability selection", cycleCount);
                var availableCapabilities = await _capabilitySelectionService.GetAvailableCapabilitiesAsync();
                var selectedCapability = await _capabilitySelectionService.SelectCapabilityAsync(
                    refinedInput, 
                    conversationHistory, 
                    systemContext, 
                    availableCapabilities, 
                    allToolExecutionResults, 
                    cancellationToken);
                    
                // 마지막 선택된 capability 저장
                finalSelectedCapability = selectedCapability;

                // 작업 완료 체크: TaskCompletion이 선택되면 종료
                if (selectedCapability.Type == SystemCapabilityType.TaskCompletion)
                {
                    _logger.LogInformation("Cycle {CycleCount} - Task completion detected, generating final response", cycleCount);
                    
                    // Step 5: Final Response Generation
                    finalResponse = await _responseGenerationService.GenerateResponseAsync(
                        refinedInput, 
                        selectedCapability, 
                        conversationHistory, 
                        allToolExecutionResults, 
                        systemContext, 
                        cancellationToken);
                    break;
                }

                // Step 3 & 4: Tool Execution (if needed)
                if (selectedCapability.Type == SystemCapabilityType.McpTool)
                {
                    var toolExecution = await ExecuteToolAsync(
                        selectedCapability, 
                        refinedInput, 
                        conversationHistory, 
                        systemContext, 
                        cycleCount, 
                        cancellationToken);
                    
                    if (toolExecution != null)
                    {
                        allToolExecutionResults.Add(toolExecution);
                        
                        // 도구 실행 결과를 다음 사이클의 입력으로 사용
                        currentInput = $"이전 작업 결과: {toolExecution.Result}. 추가 작업이 필요한지 판단하고 다음 단계를 진행해주세요.";
                        
                        // 도구 실행 결과를 대화 이력에 추가 (기존 방식)
                        var toolMessage = new ConversationMessage(MessageRole.Assistant, 
                            $"도구 '{toolExecution.ToolName}' 실행 완료. 결과: {toolExecution.Result}");
                        conversation.AddMessage(toolMessage);
                        
                        // 다음 사이클로 계속 진행
                        continue;
                    }
                    else
                    {
                        // 도구 실행 실패시에도 다음 사이클로 진행하여 다른 방법을 시도
                        currentInput = "이전 도구 실행이 실패했습니다. 다른 방법을 시도하거나 작업을 완료해주세요.";
                        continue;
                    }
                }
                else
                {
                    // MCP 도구가 아닌 경우 (SimpleChat, IntentClarification 등) 최종 응답 생성
                    _logger.LogInformation("Cycle {CycleCount} - Non-tool capability selected, generating response", cycleCount);
                    
                    finalResponse = await _responseGenerationService.GenerateResponseAsync(
                        refinedInput, 
                        selectedCapability, 
                        conversationHistory, 
                        allToolExecutionResults, 
                        systemContext, 
                        cancellationToken);
                    break;
                }
            }

            // 최대 사이클 도달 시 강제 종료 응답
            if (cycleCount >= maxCycles && string.IsNullOrEmpty(finalResponse))
            {
                _logger.LogWarning("Maximum cycles ({MaxCycles}) reached for conversation {ConversationId}", maxCycles, request.ConversationId);
                
                var taskCompletionCapability = new SystemCapability(
                    type: SystemCapabilityType.TaskCompletion,
                    description: "Maximum processing cycles reached",
                    reasoning: "최대 처리 사이클에 도달하여 작업을 완료합니다.",
                    parameters: new Dictionary<string, object>());

                var completionRefinedInput = new RefinedInput(
                    originalInput: "작업을 완료하고 최종 응답을 생성합니다.",
                    clarifiedIntent: "최대 처리 사이클에 도달하여 작업을 완료합니다.",
                    refinedQuery: "작업을 완료하고 최종 응답을 생성합니다.",
                    intentConfidence: ConfidenceLevel.High);

                finalResponse = await _responseGenerationService.GenerateResponseAsync(
                    completionRefinedInput, 
                    taskCompletionCapability, 
                    conversation.GetMessages(), 
                    allToolExecutionResults, 
                    systemContext, 
                    cancellationToken);
            }

            // Add final assistant message to conversation (기존 방식)
            var finalAssistantMessage = new ConversationMessage(MessageRole.Assistant, finalResponse);
            conversation.AddMessage(finalAssistantMessage);

            // Save conversation (기존 방식)
            await _conversationRepository.SaveAsync(conversation, cancellationToken);
            
            // TODO: 나중에 요약 기능 활성화 시 다음 코드 사용
            // await CompleteTurnWithSummaryAsync(
            //     request.ConversationId,
            //     turnNumber,
            //     request.Message,
            //     initialRefinedInput,
            //     finalSelectedCapability,
            //     allToolExecutionResults,
            //     finalResponse,
            //     systemContext,
            //     cancellationToken);

            _logger.LogInformation("Multi-cycle pipeline completed after {CycleCount} cycles for conversation {ConversationId}", 
                cycleCount, request.ConversationId);
            
            return AgentResponse.Success(finalResponse, request.ConversationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Multi-cycle pipeline failed for conversation {ConversationId}", request.ConversationId);
            return AgentResponse.Failure($"Failed to process request: {ex.Message}", request.ConversationId);
        }
    }

    private async Task<ToolExecution?> ExecuteToolAsync(
        SystemCapability selectedCapability,
        RefinedInput refinedInput,
        IReadOnlyList<ConversationMessage> conversationHistory,
        string systemContext,
        int cycleCount,
        CancellationToken cancellationToken)
    {
        // Extract tool name from selected capability parameters
        string toolName = selectedCapability.Parameters.TryGetValue("tool_name", out var toolNameObj) 
            ? toolNameObj.ToString() ?? "Echo_Echo"
            : "Echo_Echo";
            
        _logger.LogInformation("Cycle {CycleCount} - Step 3: Parameter generation for tool: {ToolName}", cycleCount, toolName);
        
        var availableTools = await _toolExecutor.GetAvailableToolsAsync(cancellationToken);
        var selectedTool = availableTools.FirstOrDefault(t => t.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase));
        
        if (selectedTool != null)
        {
            // Generate parameters using ParameterGenerationService
            var parameters = await _parameterGenerationService.GenerateParametersAsync(
                selectedTool.Name,
                selectedTool,
                refinedInput,
                conversationHistory,
                systemContext,
                cancellationToken);
            
            _logger.LogInformation("Cycle {CycleCount} - Generated parameters for tool {ToolName}: {@Parameters}", 
                cycleCount, toolName, parameters);
            
            // Execute the tool
            _logger.LogInformation("Cycle {CycleCount} - Step 4: Tool execution", cycleCount);
            var startTime = DateTime.UtcNow;
            var toolCall = await _toolExecutor.ExecuteAsync(selectedTool.Name, parameters, cancellationToken);
            var endTime = DateTime.UtcNow;
            
            var toolExecution = new ToolExecution 
            { 
                ToolName = selectedTool.Name, 
                Parameters = parameters, 
                Result = toolCall.Result ?? string.Empty, 
                IsSuccess = toolCall.IsSuccess, 
                StartTime = startTime,
                EndTime = endTime
            };
            
            _logger.LogInformation("Cycle {CycleCount} - Tool {ToolName} executed successfully", cycleCount, toolName);
            return toolExecution;
        }
        else
        {
            _logger.LogWarning("Cycle {CycleCount} - Tool {ToolName} not found in available tools", cycleCount, toolName);
            
            // Fallback to Echo tool for testing
            var echoTool = availableTools.FirstOrDefault(t => t.Name.Contains("echo", StringComparison.OrdinalIgnoreCase));
            if (echoTool != null)
            {
                var fallbackParameters = new Dictionary<string, object>
                {
                    {"message", $"Tool {toolName} not found, using Echo fallback"}
                };
                
                _logger.LogInformation("Cycle {CycleCount} - Step 4: Tool execution (fallback to Echo)", cycleCount);
                var toolCall = await _toolExecutor.ExecuteAsync(echoTool.Name, fallbackParameters, cancellationToken);
                
                return new ToolExecution 
                { 
                    ToolName = echoTool.Name, 
                    Parameters = fallbackParameters, 
                    Result = toolCall.Result ?? string.Empty, 
                    IsSuccess = toolCall.IsSuccess, 
                    StartTime = DateTime.UtcNow,
                    EndTime = DateTime.UtcNow
                };
            }
        }

        return null;
    }
    
    // TODO: 요약 기능 활성화 시 주석 해제 
    // /// <summary>
    // /// 턴 완료 및 LLM 요약 저장
    // /// </summary>
    // private async Task CompleteTurnWithSummaryAsync(
    //     string conversationId,
    //     int turnNumber,
    //     string originalInput,
    //     RefinedInput? initialRefinedInput,
    //     SystemCapability? finalSelectedCapability,
    //     List<ToolExecution> allToolExecutionResults,
    //     string finalResponse,
    //     string systemContext,
    //     CancellationToken cancellationToken)
    // {
    //     try
    //     {
    //         // 최종 선택된 capability가 없으면 TaskCompletion으로 설정
    //         if (finalSelectedCapability == null)
    //         {
    //             finalSelectedCapability = new SystemCapability(
    //                 type: SystemCapabilityType.TaskCompletion,
    //                 description: "Task completed",
    //                 reasoning: "작업 완료",
    //                 parameters: new Dictionary<string, object>());
    //         }
    //         
    //         // LLM을 사용하여 턴 요약 생성
    //         var turnSummary = await _conversationSummaryService.SummarizeTurnAsync(
    //             turnNumber,
    //             originalInput,
    //             initialRefinedInput ?? new RefinedInput(
    //                 originalInput: originalInput,
    //                 clarifiedIntent: "직접 응답",
    //                 refinedQuery: originalInput,
    //                 intentConfidence: ConfidenceLevel.High),
    //             finalSelectedCapability,
    //             allToolExecutionResults,
    //             finalResponse,
    //             systemContext,
    //             cancellationToken);
    //         
    //         // 요약과 함께 턴 완료 처리
    //         await _summarizedConversationRepository.CompleteTurnWithSummaryAsync(conversationId, turnSummary, cancellationToken);
    //         
    //         _logger.LogInformation(
    //             "Turn {TurnNumber} completed and summarized for conversation {ConversationId}", 
    //             turnNumber, conversationId);
    //             
    //         // 5턴마다 통합 요약 생성
    //         if (turnNumber % 5 == 0)
    //         {
    //             await GenerateConsolidatedSummaryAsync(conversationId, systemContext, cancellationToken);
    //         }
    //     }
    //     catch (Exception ex)
    //     {
    //         _logger.LogError(ex, "Failed to complete turn with summary for conversation {ConversationId} (turn {Turn})", 
    //             conversationId, turnNumber);
    //         throw;
    //     }
    // }
    // 
    // /// <summary>
    // /// 5턴마다 통합 요약 생성
    // /// </summary>
    // private async Task GenerateConsolidatedSummaryAsync(
    //     string conversationId,
    //     string systemContext, 
    //     CancellationToken cancellationToken)
    // {
    //     try
    //     {
    //         _logger.LogInformation("Generating consolidated summary for conversation {ConversationId}", conversationId);
    //         
    //         // 현재 개별 요약들 가져오기
    //         var conversationSummary = await _conversationSummaryService.GetConversationSummaryAsync(conversationId, cancellationToken);
    //         
    //         if (conversationSummary.IndividualTurns.Count >= 5)
    //         {
    //             var consolidatedSummary = await _conversationSummaryService.ConsolidateTurnsAsync(
    //                 conversationSummary.IndividualTurns, 
    //                 systemContext, 
    //                 cancellationToken);
    //             
    //             await _summarizedConversationRepository.UpdateConsolidatedSummaryAsync(
    //                 conversationId, 
    //                 consolidatedSummary, 
    //                 cancellationToken);
    //                 
    //             _logger.LogInformation("Consolidated summary generated for conversation {ConversationId}", conversationId);
    //         }
    //     }
    //     catch (Exception ex)
    //     {
    //         _logger.LogError(ex, "Failed to generate consolidated summary for conversation {ConversationId}", conversationId);
    //         // 비치명적 에러로 처리 (진행에 영향을 주지 않음)
    //     }
    // }
}