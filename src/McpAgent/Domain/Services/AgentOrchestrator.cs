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
    private readonly IToolExecutor _toolExecutor;

    public AgentOrchestrator(
        ILogger<AgentOrchestrator> logger,
        IInputRefinementService inputRefinementService,
        ICapabilitySelectionService capabilitySelectionService,
        IParameterGenerationService parameterGenerationService,
        IResponseGenerationService responseGenerationService,
        IConversationRepository conversationRepository,
        IToolExecutor toolExecutor)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _inputRefinementService = inputRefinementService ?? throw new ArgumentNullException(nameof(inputRefinementService));
        _capabilitySelectionService = capabilitySelectionService ?? throw new ArgumentNullException(nameof(capabilitySelectionService));
        _parameterGenerationService = parameterGenerationService ?? throw new ArgumentNullException(nameof(parameterGenerationService));
        _responseGenerationService = responseGenerationService ?? throw new ArgumentNullException(nameof(responseGenerationService));
        _conversationRepository = conversationRepository ?? throw new ArgumentNullException(nameof(conversationRepository));
        _toolExecutor = toolExecutor ?? throw new ArgumentNullException(nameof(toolExecutor));
    }

    public async Task<AgentResponse> ProcessRequestAsync(AgentRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting multi-cycle pipeline for conversation {ConversationId}", request.ConversationId);

            // Get or create conversation
            var conversation = await _conversationRepository.GetByIdAsync(request.ConversationId, cancellationToken)
                ?? await _conversationRepository.CreateAsync(request.ConversationId, cancellationToken);

            // Add user message to conversation
            var userMessage = new ConversationMessage(MessageRole.User, request.Message);
            conversation.AddMessage(userMessage);

            var systemContext = "AI 에이전트";
            var allToolExecutionResults = new List<ToolExecution>();
            var cycleCount = 0;
            const int maxCycles = 5; // 무한 루프 방지

            string currentInput = request.Message;
            string finalResponse = "";

            while (cycleCount < maxCycles)
            {
                cycleCount++;
                _logger.LogInformation("Starting cycle {CycleCount} for conversation {ConversationId}", cycleCount, request.ConversationId);

                var conversationHistory = conversation.GetMessages();

                // Step 1: Input Refinement
                _logger.LogInformation("Cycle {CycleCount} - Step 1: Input refinement", cycleCount);
                var refinedInput = await _inputRefinementService.RefineInputAsync(
                    currentInput, 
                    conversationHistory, 
                    systemContext, 
                    cancellationToken);

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
                        
                        // 도구 실행 결과를 대화 이력에 추가
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

            // Add final assistant message to conversation
            var finalAssistantMessage = new ConversationMessage(MessageRole.Assistant, finalResponse);
            conversation.AddMessage(finalAssistantMessage);

            // Save conversation
            await _conversationRepository.SaveAsync(conversation, cancellationToken);

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
}