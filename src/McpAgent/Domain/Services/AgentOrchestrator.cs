using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;
using McpAgent.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    private readonly IHtmlVisualizationService _htmlVisualizationService;

    private readonly IDisplayProcess _displayProcess;
    private readonly AgentSettings _agentSettings;

    public AgentOrchestrator(
        ILogger<AgentOrchestrator> logger,
        IInputRefinementService inputRefinementService,
        ICapabilitySelectionService capabilitySelectionService,
        IParameterGenerationService parameterGenerationService,
        IResponseGenerationService responseGenerationService,
        IConversationRepository conversationRepository,
        IToolExecutor toolExecutor,
        IHtmlVisualizationService htmlVisualizationService,
        IDisplayProcess displayProcess,
        IOptions<AgentSettings> agentSettings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _inputRefinementService = inputRefinementService ?? throw new ArgumentNullException(nameof(inputRefinementService));
        _capabilitySelectionService = capabilitySelectionService ?? throw new ArgumentNullException(nameof(capabilitySelectionService));
        _parameterGenerationService = parameterGenerationService ?? throw new ArgumentNullException(nameof(parameterGenerationService));
        _responseGenerationService = responseGenerationService ?? throw new ArgumentNullException(nameof(responseGenerationService));
        _conversationRepository = conversationRepository ?? throw new ArgumentNullException(nameof(conversationRepository));
        _toolExecutor = toolExecutor ?? throw new ArgumentNullException(nameof(toolExecutor));
        _htmlVisualizationService = htmlVisualizationService ?? throw new ArgumentNullException(nameof(htmlVisualizationService));
        _displayProcess = displayProcess ?? throw new ArgumentNullException(nameof(displayProcess));
        _agentSettings = agentSettings?.Value ?? throw new ArgumentNullException(nameof(agentSettings));
    }

    public async Task<AgentResponse> ProcessRequestAsync(AgentRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting multi-cycle pipeline for conversation {ConversationId}", request.ConversationId);

            var conversation = await _conversationRepository.GetByIdAsync(request.ConversationId, cancellationToken)
                ?? await _conversationRepository.CreateAsync(request.ConversationId, cancellationToken);

            // Add user message to conversation (기존 방식)
            var userMessage = new ConversationMessage(MessageRole.User, request.Message);
            conversation.AddMessage(userMessage);

            var systemContext = $"AI 에이전트 - 대화ID: {request.ConversationId}";

            var allToolExecutionResults = new List<ToolExecution>();
            var cycleCount = 0;
            var maxCycles = _agentSettings.MaxToolChainIterations;

            string currentInput = request.Message;
            string finalResponse = "";

            // 턴 번호 계산 (기존 방식)
            var turnNumber = conversation.GetMessages().Count(m => m.Role == MessageRole.User);

            // 초기 입력 정제 (처음에 한 번만 수행) - 사용자 원본 의도 보존
            RefinedInput? initialRefinedInput = null;
            SystemCapability? finalSelectedCapability = null;

            // 누적 계획 관리: 각 사이클마다 새로운 계획이 추가됨
            var cumulativePlans = new List<string>();

            while (cycleCount < maxCycles)
            {
                cycleCount++;
                _logger.LogInformation("Starting cycle {CycleCount} for conversation {ConversationId}", cycleCount, request.ConversationId);

                // 각 사이클마다 최신 대화 이력 가져오기 (도구 실행 결과가 추가될 수 있으므로)
                var conversationHistory = conversation.GetRecentMessages(_agentSettings.MaxHistoryLength);

                RefinedInput refinedInput;

                if (cycleCount == 1)
                {
                    // 첫 번째 사이클: 사용자 원본 요청을 정제
                    _logger.LogInformation("Cycle {CycleCount} - Step 1: Initial input refinement", cycleCount);
                    _displayProcess.DisplayProcess($"사용자의 의도를 파악 중입니다... [사이클: {cycleCount}]");
                    refinedInput = await _inputRefinementService.RefineInputAsync(
                        currentInput,
                        conversationHistory,
                        systemContext,
                        cancellationToken);
                    initialRefinedInput = refinedInput;

                    // 첫 번째 계획을 누적 계획에 추가
                    if (!string.IsNullOrEmpty(refinedInput.SuggestedPlan))
                    {
                        cumulativePlans.Add($"[사이클 {cycleCount}] {refinedInput.SuggestedPlan}");
                        _logger.LogInformation("Initial plan added: {Plan}", refinedInput.SuggestedPlan);
                    }
                }
                else
                {
                    // 후속 사이클: 원본 의도를 유지하면서 현재 상황 반영하고 누적 계획 포함
                    _logger.LogInformation("Cycle {CycleCount} - Step 1: Context-aware refinement with cumulative plans", cycleCount);

                    // 누적 계획 정보 구성
                    var allPlansInfo = cumulativePlans.Count > 0
                        ? $"\n\n[누적된 제안 계획들]\n{string.Join("\n", cumulativePlans)}"
                        : "";

                    // 원본 사용자 의도와 현재 도구 실행 결과, 누적 계획을 결합한 컨텍스트 생성
                    var enhancedContext = $"{systemContext}\n\n[원본 사용자 요청]\n의도: {initialRefinedInput?.ClarifiedIntent ?? "확인 필요"}\n요청: {initialRefinedInput?.RefinedQuery ?? currentInput}{allPlansInfo}\n\n[현재 상황]\n{currentInput}";
                    _displayProcess.DisplayProcess($"이전 조사를 바탕으로 사용자의 의도를 다시 파악 중입니다... [사이클: {cycleCount}]");
                    refinedInput = await _inputRefinementService.RefineInputAsync(
                        currentInput,
                        conversationHistory,
                        enhancedContext,
                        cancellationToken);

                    // 새로운 계획이 제안되었으면 누적 계획에 추가
                    if (!string.IsNullOrEmpty(refinedInput.SuggestedPlan))
                    {
                        cumulativePlans.Add($"[사이클 {cycleCount}] {refinedInput.SuggestedPlan}");
                        _logger.LogInformation("Additional plan added for cycle {Cycle}: {Plan}", cycleCount, refinedInput.SuggestedPlan);
                    }
                }

                // Step 2: Capability Selection
                _logger.LogInformation("Cycle {CycleCount} - Step 2: Capability selection", cycleCount);

                // 누적 계획 정보가 포함된 시스템 컨텍스트 생성
                var cumulativePlansInfo = cumulativePlans.Count > 0
                    ? $"\n\n[진행 계획 상태]\n{string.Join("\n", cumulativePlans)}"
                    : "";
                var enhancedSystemContext = $"{systemContext}{cumulativePlansInfo}";

                // Get available capabilities
                _displayProcess.DisplayProcess($"분석된 의도를 기준으로 사용할 기능을 선택 중입니다... [사이클: {cycleCount}]");
                var selectedCapability = await _capabilitySelectionService.SelectCapabilityAsync(
                    refinedInput,
                    conversationHistory,
                    enhancedSystemContext,
                    allToolExecutionResults,
                    cancellationToken);

                // 마지막 선택된 capability 저장
                finalSelectedCapability = selectedCapability;

                // 작업 완료 체크: TaskCompletion이 선택되면 종료
                if (selectedCapability.Type == SystemCapabilityType.TaskCompletion)
                {
                    _logger.LogInformation("Cycle {CycleCount} - Task completion detected, generating final response", cycleCount);

                    // Step 5: Final Response Generation
                    _displayProcess.DisplayProcess($"사용자 응답을 생성 중입니다... [사이클: {cycleCount}]");
                    finalResponse = await _responseGenerationService.GenerateResponseAsync(
                        refinedInput,
                        selectedCapability,
                        conversationHistory,
                        allToolExecutionResults,
                        systemContext,
                        cancellationToken);
                    break;
                }

                // Step 3 & 4: HTML Visualization Execution
                if (selectedCapability.Type == SystemCapabilityType.HtmlVisualization)
                {
                    _displayProcess.DisplayProcess($"HTML 시각화를 생성합니다... [사이클: {cycleCount}]");
                    var toolExecution = await ExecuteToolAsync(
                        selectedCapability,
                        refinedInput,
                        conversationHistory,
                        systemContext,
                        cumulativePlans,
                        cycleCount,
                        cancellationToken);

                    if (toolExecution != null)
                    {
                        allToolExecutionResults.Add(toolExecution);

                        // HTML 시각화 완료 후 최종 응답 생성
                        _displayProcess.DisplayProcess($"HTML 시각화가 완료되었습니다. 최종 응답을 생성 중입니다... [사이클: {cycleCount}]");
                        
                        finalResponse = await _responseGenerationService.GenerateResponseAsync(
                            refinedInput,
                            new SystemCapability(SystemCapabilityType.TaskCompletion, "HTML 시각화 완료", "HTML 시각화가 성공적으로 생성되었습니다."),
                            conversationHistory,
                            allToolExecutionResults,
                            systemContext,
                            cancellationToken);
                        break;
                    }
                }

                // Step 3 & 4: Tool Execution (if needed)
                if (selectedCapability.Type == SystemCapabilityType.McpTool)
                {
                    _displayProcess.DisplayProcess($"MCP 도구를 사용 합니다... [사이클: {cycleCount}]");
                    var toolExecution = await ExecuteToolAsync(
                        selectedCapability,
                        refinedInput,
                        conversationHistory,
                        systemContext,
                        cumulativePlans,
                        cycleCount,
                        cancellationToken);

                    if (toolExecution != null)
                    {
                        allToolExecutionResults.Add(toolExecution);

                        // 도구 실행 결과를 다음 사이클의 입력으로 사용하되, 원본 의도와 계획을 명시적으로 포함
                        var planContext = !string.IsNullOrEmpty(initialRefinedInput?.SuggestedPlan)
                            ? $"\n원본 제안 계획: {initialRefinedInput.SuggestedPlan}"
                            : "";
                        currentInput = $"[원본 사용자 요청 목표: {initialRefinedInput?.ClarifiedIntent ?? "사용자 요청 처리"}]{planContext}\n\n이전 도구 '{toolExecution.ToolName}' 실행 결과: {toolExecution.Result}\n\n원본 사용자 요청과 제안된 계획에 따라 추가 작업이 필요한지 판단하고 다음 단계를 진행해주세요.";

                        // 도구 실행 결과를 대화 이력에 추가 (기존 방식)
                        var toolMessage = new ConversationMessage(MessageRole.Assistant,
                            $"도구 '{toolExecution.ToolName}' 실행 완료. 결과: {toolExecution.Result}");
                        conversation.AddMessage(toolMessage);

                        // 도구 체이닝이 비활성화된 경우 바로 응답 생성
                        if (!_agentSettings.EnableToolChaining)
                        {
                            _logger.LogInformation("Cycle {CycleCount} - Tool chaining disabled, generating final response", cycleCount);
                            _displayProcess.DisplayProcess($"도구 체이닝이 비활성화되어 최종 응답을 생성합니다... [사이클: {cycleCount}]");
                            
                            var taskCompletionCapability = new SystemCapability(
                                type: SystemCapabilityType.TaskCompletion,
                                description: "Tool executed, generating final response",
                                reasoning: "도구 체이닝이 비활성화되어 단일 도구 실행 후 완료합니다.",
                                parameters: new Dictionary<string, object>());
                            
                            finalResponse = await _responseGenerationService.GenerateResponseAsync(
                                refinedInput,
                                taskCompletionCapability,
                                conversationHistory,
                                allToolExecutionResults,
                                systemContext,
                                cancellationToken);
                            break;
                        }

                        // 다음 사이클로 계속 진행
                        continue;
                    }
                    else
                    {
                        // 도구 실행 실패시에도 다음 사이클로 진행하여 다른 방법을 시도하되, 원본 의도와 계획 유지
                        var planContext = !string.IsNullOrEmpty(initialRefinedInput?.SuggestedPlan)
                            ? $"\n원본 제안 계획: {initialRefinedInput.SuggestedPlan}"
                            : "";
                        currentInput = $"[원본 사용자 요청 목표: {initialRefinedInput?.ClarifiedIntent ?? "사용자 요청 처리"}]{planContext}\n\n이전 도구 실행이 실패했습니다. 원본 사용자 요청과 제안된 계획에 따라 다른 방법을 시도하거나 작업을 완료해주세요.";
                        continue;
                    }
                }
                else
                {
                    // TaskPlanning인 경우 계속 진행, 다른 기능은 최종 응답 생성
                    if (selectedCapability.Type == SystemCapabilityType.TaskPlanning)
                    {
                        _logger.LogInformation("Cycle {CycleCount} - Task planning detected, continuing to next cycle", cycleCount);
                        _displayProcess.DisplayProcess($"계획을 수립하고 다음 단계를 진행합니다... [사이클: {cycleCount}]");
                        
                        // 계획 정보를 다음 사이클 입력으로 설정
                        var planContext = !string.IsNullOrEmpty(initialRefinedInput?.SuggestedPlan)
                            ? $"\n원본 제안 계획: {initialRefinedInput.SuggestedPlan}"
                            : "";
                        
                        currentInput = $"[원본 사용자 요청 목표: {initialRefinedInput?.ClarifiedIntent ?? "사용자 요청 처리"}]{planContext}\n\n계획이 수립되었습니다. 이제 계획에 따라 구체적인 작업을 수행해주세요.";
                        
                        // 계획 메시지를 대화 이력에 추가
                        var planningMessage = new ConversationMessage(MessageRole.Assistant,
                            $"계획 수립 완료: {refinedInput.SuggestedPlan ?? "구체적인 계획이 생성되었습니다."}");
                        conversation.AddMessage(planningMessage);
                        
                        continue;
                    }
                    else
                    {
                        // SimpleChat, IntentClarification 등의 경우만 최종 응답 생성
                        // HtmlVisualization은 이미 위에서 처리됨
                        if (selectedCapability.Type == SystemCapabilityType.SimpleChat ||
                            selectedCapability.Type == SystemCapabilityType.IntentClarification ||
                            selectedCapability.Type == SystemCapabilityType.ErrorHandling)
                        {
                            _logger.LogInformation("Cycle {CycleCount} - Non-tool capability selected, generating response", cycleCount);
                            _displayProcess.DisplayProcess($"사용자 응답을 생성 중입니다... [사이클: {cycleCount}]");
                            finalResponse = await _responseGenerationService.GenerateResponseAsync(
                                refinedInput,
                                selectedCapability,
                                conversationHistory,
                                allToolExecutionResults,
                                systemContext,
                                cancellationToken);
                            break;
                        }
                        else
                        {
                            // 예상하지 못한 능력 타입의 경우 로그 출력 후 계속 진행
                            _logger.LogWarning("Cycle {CycleCount} - Unexpected capability type: {Type}, continuing to next cycle", 
                                cycleCount, selectedCapability.Type);
                            continue;
                        }
                    }
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
                _displayProcess.DisplayProcess($"사용자 응답을 생성 중입니다... [사이클: {cycleCount}]");
                finalResponse = await _responseGenerationService.GenerateResponseAsync(
                    completionRefinedInput,
                    taskCompletionCapability,
                    conversation.GetMessages(),
                    allToolExecutionResults,
                    systemContext,
                    cancellationToken);
            }

            var finalAssistantMessage = new ConversationMessage(MessageRole.Assistant, finalResponse);
            conversation.AddMessage(finalAssistantMessage);

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
        List<string> cumulativePlans,
        int cycleCount,
        CancellationToken cancellationToken)
    {
        // HTML 시각화 처리
        if (selectedCapability.Type == SystemCapabilityType.HtmlVisualization)
        {
            return await ExecuteHtmlVisualizationAsync(selectedCapability, refinedInput, cycleCount, cancellationToken);
        }

        // 기존 MCP 도구 처리
        var toolName = ExtractToolName(selectedCapability);
        _logger.LogInformation("Cycle {CycleCount} - Step 3: Parameter generation for tool: {ToolName}", cycleCount, toolName);

        var selectedTool = await GetToolByNameAsync(toolName, cancellationToken);
        if (selectedTool == null)
        {
            return CreateFailedToolExecution(toolName, cycleCount);
        }

        return await ExecuteValidToolAsync(selectedTool, refinedInput, conversationHistory, systemContext, cumulativePlans, cycleCount, cancellationToken);
    }

    private string ExtractToolName(SystemCapability selectedCapability)
    {
        return selectedCapability.Parameters.TryGetValue("tool_name", out var toolNameObj)
            ? toolNameObj.ToString() ?? "UnknownTool"
            : "UnknownTool";
    }

    private async Task<ToolDefinition?> GetToolByNameAsync(string toolName, CancellationToken cancellationToken)
    {
        var availableTools = await _toolExecutor.GetAvailableToolsAsync(cancellationToken);
        return availableTools.FirstOrDefault(t => t.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase));
    }

    private ToolExecution CreateFailedToolExecution(string toolName, int cycleCount)
    {
        _logger.LogError("Cycle {CycleCount} - Tool {ToolName} not found in available tools", cycleCount, toolName);

        return new ToolExecution
        {
            ToolName = toolName,
            Parameters = new Dictionary<string, object>(),
            Result = $"도구 '{toolName}'을(를) 찾을 수 없습니다.",
            IsSuccess = false,
            StartTime = DateTime.UtcNow,
            EndTime = DateTime.UtcNow
        };
    }

    private async Task<ToolExecution> ExecuteValidToolAsync(
        ToolDefinition selectedTool,
        RefinedInput refinedInput,
        IReadOnlyList<ConversationMessage> conversationHistory,
        string systemContext,
        List<string> cumulativePlans,
        int cycleCount,
        CancellationToken cancellationToken)
    {
        Dictionary<string, object> parameters = new();

        if (selectedTool.Parameters.Any())
        {
            var enhancedSystemContext = BuildEnhancedSystemContext(systemContext, cumulativePlans);
            parameters = await _parameterGenerationService.GenerateParametersAsync(selectedTool.Name, selectedTool,
                               refinedInput, conversationHistory, enhancedSystemContext, cancellationToken);

            _logger.LogInformation("Cycle {CycleCount} - Generated parameters for tool {ToolName}: {@Parameters}",
                cycleCount, selectedTool.Name, parameters);
        }

        return await ExecuteToolWithParametersAsync(selectedTool, parameters, cycleCount, cancellationToken);
    }

    private string BuildEnhancedSystemContext(string systemContext, List<string> cumulativePlans)
    {
        var cumulativePlansInfo = cumulativePlans.Count > 0
            ? $"\n\n[진행 계획 상태]\n{string.Join("\n", cumulativePlans)}"
            : "";
        return $"{systemContext}{cumulativePlansInfo}";
    }

    private async Task<ToolExecution> ExecuteToolWithParametersAsync(
        ToolDefinition selectedTool,
        Dictionary<string, object> parameters,
        int cycleCount,
        CancellationToken cancellationToken)
    {
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

        _logger.LogInformation("Cycle {CycleCount} - Tool {ToolName} executed with success: {IsSuccess}",
            cycleCount, selectedTool.Name, toolCall.IsSuccess);

        return toolExecution;
    }

    private async Task<ToolExecution> ExecuteHtmlVisualizationAsync(
        SystemCapability selectedCapability,
        RefinedInput refinedInput,
        int cycleCount,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Cycle {CycleCount} - Executing HTML visualization", cycleCount);
        
        var startTime = DateTime.UtcNow;
        
        try
        {
            // 시각화 데이터 추출 (선택적)
            var data = selectedCapability.Parameters.TryGetValue("data", out var dataObj)
                ? dataObj?.ToString()
                : null;

            // HTML 시각화 생성 및 브라우저에서 열기
            var result = await _htmlVisualizationService.CreateAndOpenVisualizationAsync(
                refinedInput.RefinedQuery, 
                data, 
                cancellationToken);

            var endTime = DateTime.UtcNow;

            var toolExecution = new ToolExecution
            {
                ToolName = "HtmlVisualization",
                Parameters = selectedCapability.Parameters,
                Result = result.IsSuccess 
                    ? $"HTML 시각화가 성공적으로 생성되었습니다. 파일 경로: {result.FilePath}"
                    : $"HTML 시각화 생성 실패: {result.ErrorMessage}",
                IsSuccess = result.IsSuccess,
                StartTime = startTime,
                EndTime = endTime
            };

            _logger.LogInformation("Cycle {CycleCount} - HTML visualization executed with success: {IsSuccess}", 
                cycleCount, result.IsSuccess);

            return toolExecution;
        }
        catch (Exception ex)
        {
            var endTime = DateTime.UtcNow;
            
            _logger.LogError(ex, "Cycle {CycleCount} - HTML visualization execution failed", cycleCount);

            return new ToolExecution
            {
                ToolName = "HtmlVisualization",
                Parameters = selectedCapability.Parameters,
                Result = $"HTML 시각화 실행 중 오류 발생: {ex.Message}",
                IsSuccess = false,
                StartTime = startTime,
                EndTime = endTime
            };
        }
    }
}