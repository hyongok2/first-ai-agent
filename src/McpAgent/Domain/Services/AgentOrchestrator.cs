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
            _logger.LogInformation("Starting multi-step pipeline for conversation {ConversationId}", request.ConversationId);

            // Get or create conversation
            var conversation = await _conversationRepository.GetByIdAsync(request.ConversationId, cancellationToken)
                ?? await _conversationRepository.CreateAsync(request.ConversationId, cancellationToken);

            // Add user message to conversation
            var userMessage = new ConversationMessage(MessageRole.User, request.Message);
            conversation.AddMessage(userMessage);

            var conversationHistory = conversation.GetMessages();
            var systemContext = "AI 에이전트";

            // Step 1: Input Refinement (using input-refinement.txt)
            _logger.LogInformation("Step 1: Input refinement");
            var refinedInput = await _inputRefinementService.RefineInputAsync(
                request.Message, 
                conversationHistory, 
                systemContext, 
                cancellationToken);

            // Step 2: Capability Selection (using capability-selection.txt)
            _logger.LogInformation("Step 2: Capability selection");
            var availableCapabilities = await _capabilitySelectionService.GetAvailableCapabilitiesAsync();
            var selectedCapability = await _capabilitySelectionService.SelectCapabilityAsync(
                refinedInput, 
                conversationHistory, 
                systemContext, 
                availableCapabilities, 
                null, 
                cancellationToken);

            // Step 3: Parameter Generation & Step 4: Tool Execution (if MCP tool needed)
            IReadOnlyList<ToolExecution>? toolExecutionResults = null;
            if (selectedCapability.Type == SystemCapabilityType.McpTool)
            {
                // Extract tool name from selected capability parameters
                string toolName = selectedCapability.Parameters.TryGetValue("tool_name", out var toolNameObj) 
                    ? toolNameObj.ToString() ?? "Echo_Echo"
                    : "Echo_Echo";
                    
                _logger.LogInformation("Step 3: Parameter generation for tool: {ToolName}", toolName);
                
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
                    
                    _logger.LogInformation("Generated parameters for tool {ToolName}: {@Parameters}", toolName, parameters);
                    
                    // Step 4: Execute the tool
                    _logger.LogInformation("Step 4: Tool execution");
                    var startTime = DateTime.UtcNow;
                    var toolCall = await _toolExecutor.ExecuteAsync(selectedTool.Name, parameters, cancellationToken);
                    var endTime = DateTime.UtcNow;
                    
                    toolExecutionResults = new List<ToolExecution>
                    {
                        new ToolExecution 
                        { 
                            ToolName = selectedTool.Name, 
                            Parameters = parameters, 
                            Result = toolCall.Result ?? string.Empty, 
                            IsSuccess = toolCall.IsSuccess, 
                            StartTime = startTime,
                            EndTime = endTime
                        }
                    };
                    
                    _logger.LogInformation("Tool {ToolName} executed successfully", toolName);
                }
                else
                {
                    _logger.LogWarning("Tool {ToolName} not found in available tools", toolName);
                    
                    // Fallback to Echo tool for testing
                    var echoTool = availableTools.FirstOrDefault(t => t.Name.Contains("echo", StringComparison.OrdinalIgnoreCase));
                    if (echoTool != null)
                    {
                        var fallbackParameters = new Dictionary<string, object>
                        {
                            {"message", $"Tool {toolName} not found, using Echo fallback"}
                        };
                        
                        _logger.LogInformation("Step 4: Tool execution (fallback to Echo)");
                        var toolCall = await _toolExecutor.ExecuteAsync(echoTool.Name, fallbackParameters, cancellationToken);
                        toolExecutionResults = new List<ToolExecution>
                        {
                            new ToolExecution 
                            { 
                                ToolName = echoTool.Name, 
                                Parameters = fallbackParameters, 
                                Result = toolCall.Result ?? string.Empty, 
                                IsSuccess = toolCall.IsSuccess, 
                                StartTime = DateTime.UtcNow,
                                EndTime = DateTime.UtcNow
                            }
                        };
                    }
                }
            }

            // Step 5: Response Generation (using response-generation.txt)
            _logger.LogInformation("Step 5: Response generation");
            var finalResponse = await _responseGenerationService.GenerateResponseAsync(
                refinedInput, 
                selectedCapability, 
                conversationHistory, 
                toolExecutionResults, 
                systemContext, 
                cancellationToken);

            // Add assistant message to conversation
            var assistantMessage = new ConversationMessage(MessageRole.Assistant, finalResponse);
            conversation.AddMessage(assistantMessage);

            // Save conversation
            await _conversationRepository.SaveAsync(conversation, cancellationToken);

            _logger.LogInformation("Multi-step pipeline completed successfully for conversation {ConversationId}", request.ConversationId);
            return AgentResponse.Success(finalResponse, request.ConversationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Multi-step pipeline failed for conversation {ConversationId}", request.ConversationId);
            return AgentResponse.Failure($"Failed to process request: {ex.Message}", request.ConversationId);
        }
    }
}