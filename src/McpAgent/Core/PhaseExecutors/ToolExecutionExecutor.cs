using System.Text.Json;
using McpAgent.Mcp;
using McpAgent.Models;
using Microsoft.Extensions.Logging;

namespace McpAgent.Core.PhaseExecutors;

public class ToolExecutionExecutor : IPhaseExecutor
{
    private readonly ILogger<ToolExecutionExecutor> _logger;
    private readonly IMcpClient _mcpClient;
    
    public int PhaseNumber => 4;
    
    public ToolExecutionExecutor(
        ILogger<ToolExecutionExecutor> logger,
        IMcpClient mcpClient)
    {
        _logger = logger;
        _mcpClient = mcpClient;
    }
    
    public async Task<PhaseResult> ExecuteAsync(ConversationState state, string userInput, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!state.PhaseHistory.TryGetValue(3, out var parameterResult))
            {
                return CreateErrorResult("Parameter generation result not found");
            }
            
            var toolCalls = ExtractToolCalls(parameterResult);
            if (!toolCalls.Any())
            {
                return CreateErrorResult("No tool calls to execute");
            }
            
            var executionResults = new List<object>();
            var allSuccessful = true;
            var hasPartialSuccess = false;
            
            foreach (var toolCall in toolCalls)
            {
                try
                {
                    _logger.LogDebug("Phase 4: Executing tool {ToolName}", toolCall.Name);
                    
                    // Chat response는 특별 처리
                    if (toolCall.Name == "chat_response")
                    {
                        var chatResult = new
                        {
                            tool_name = "chat_response",
                            result = ProcessChatResponse(userInput, state),
                            success = true,
                            execution_time = 0
                        };
                        executionResults.Add(chatResult);
                        continue;
                    }
                    
                    // MCP 도구 실행
                    var startTime = DateTime.UtcNow;
                    var result = await _mcpClient.CallToolAsync(toolCall.Name, toolCall.Arguments, cancellationToken);
                    var executionTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    
                    var toolResult = new
                    {
                        tool_name = toolCall.Name,
                        result = result,
                        success = true,
                        execution_time = executionTime
                    };
                    
                    executionResults.Add(toolResult);
                    
                    _logger.LogDebug("Tool {ToolName} executed successfully in {ExecutionTime}ms", 
                        toolCall.Name, executionTime);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing tool {ToolName}", toolCall.Name);
                    
                    var errorResult = new
                    {
                        tool_name = toolCall.Name,
                        result = $"Error: {ex.Message}",
                        success = false,
                        error = ex.Message
                    };
                    
                    executionResults.Add(errorResult);
                    allSuccessful = false;
                    hasPartialSuccess = executionResults.Any(r => 
                        r.GetType().GetProperty("success")?.GetValue(r)?.Equals(true) == true);
                }
            }
            
            var status = allSuccessful ? ExecutionStatus.Success : 
                        hasPartialSuccess ? ExecutionStatus.PartialSuccess : 
                        ExecutionStatus.Failure;
            
            return new PhaseResult
            {
                Phase = 4,
                Status = status,
                Data = new Dictionary<string, object>
                {
                    ["execution_results"] = executionResults,
                    ["total_tools_executed"] = toolCalls.Count,
                    ["successful_executions"] = executionResults.Count(r => 
                        r.GetType().GetProperty("success")?.GetValue(r)?.Equals(true) == true)
                },
                ConfidenceScore = allSuccessful ? 1.0 : hasPartialSuccess ? 0.6 : 0.0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in tool execution phase");
            return CreateErrorResult(ex.Message);
        }
    }
    
    private List<ToolCallInfo> ExtractToolCalls(PhaseResult parameterResult)
    {
        var toolCalls = new List<ToolCallInfo>();
        
        _logger.LogDebug("Phase 4: Extracting tool calls from parameter result data. Keys: {Keys}", 
            string.Join(", ", parameterResult.Data.Keys));
        
        if (parameterResult.Data.TryGetValue("tool_calls", out var toolCallsObj))
        {
            _logger.LogDebug("Phase 4: Found tool_calls object of type: {Type}", toolCallsObj?.GetType().Name ?? "null");
            
            if (toolCallsObj is List<object> toolCallsList)
            {
                _logger.LogDebug("Phase 4: Processing {Count} tool calls", toolCallsList.Count);
                
                foreach (var toolCallObj in toolCallsList)
                {
                    _logger.LogDebug("Phase 4: Processing tool call object of type: {Type}", toolCallObj?.GetType().Name ?? "null");
                    
                    if (toolCallObj is Dictionary<string, object> toolCallDict)
                    {
                        var toolCall = new ToolCallInfo
                        {
                            Name = toolCallDict.GetValueOrDefault("name")?.ToString() ?? "",
                            Arguments = toolCallDict.GetValueOrDefault("parameters") as Dictionary<string, object> ?? new(),
                            ExpectedOutputType = toolCallDict.GetValueOrDefault("expected_output_type")?.ToString() ?? "string"
                        };
                        
                        _logger.LogDebug("Phase 4: Parsed tool call - Name: {Name}, Arguments: {ArgCount}, OutputType: {OutputType}",
                            toolCall.Name, toolCall.Arguments.Count, toolCall.ExpectedOutputType);
                        
                        if (!string.IsNullOrEmpty(toolCall.Name))
                        {
                            toolCalls.Add(toolCall);
                        }
                        else
                        {
                            _logger.LogWarning("Phase 4: Tool call has empty name, skipping");
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Phase 4: Tool call object is not Dictionary<string, object>, actual type: {Type}", 
                            toolCallObj?.GetType().Name ?? "null");
                    }
                }
            }
            else
            {
                _logger.LogWarning("Phase 4: tool_calls is not List<object>, actual type: {Type}", 
                    toolCallsObj?.GetType().Name ?? "null");
            }
        }
        else
        {
            _logger.LogWarning("Phase 4: No 'tool_calls' key found in parameter result data");
        }
        
        _logger.LogInformation("Phase 4: Extracted {Count} tool calls for execution", toolCalls.Count);
        return toolCalls;
    }
    
    private string ProcessChatResponse(string userInput, ConversationState state)
    {
        // 간단한 채팅 응답 생성
        var responses = new[]
        {
            "I understand your request. How can I help you further?",
            "Thank you for your input. What would you like to do next?",
            "I'm here to assist you. Please let me know if you need anything else.",
            "Got it! Is there anything specific you'd like me to help with?"
        };
        
        var random = new Random();
        return responses[random.Next(responses.Length)];
    }
    
    private PhaseResult CreateErrorResult(string message)
    {
        return new PhaseResult
        {
            Phase = 4,
            Status = ExecutionStatus.Failure,
            ErrorMessage = message,
            Data = new Dictionary<string, object> { ["wrong_function"] = true }
        };
    }
}

public class ToolCallInfo
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, object> Arguments { get; set; } = new();
    public string ExpectedOutputType { get; set; } = "string";
}