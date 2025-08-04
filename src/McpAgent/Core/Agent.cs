using System.Text;
using System.Text.Json;
using McpAgent.Configuration;
using McpAgent.Mcp;
using McpAgent.Memory;
using McpAgent.Models;
using McpAgent.Providers;
using McpAgent.Services;
using McpAgent.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace McpAgent.Core;

public class Agent : IAgent
{
    private readonly ILogger<Agent> _logger;
    private readonly AgentConfiguration _config;
    private readonly ILlmProvider _llmProvider;
    private readonly IMcpClient _mcpClient;
    private readonly IConversationManager _conversationManager;
    private readonly IPromptService _promptService;
    private readonly IStreamingService _streamingService;
    private readonly ISessionManager _sessionManager;
    private readonly IContextManager _contextManager;
    private readonly TokenCounter _tokenCounter;

    public Agent(
        ILogger<Agent> logger,
        IOptions<AgentConfiguration> options,
        ILlmProvider llmProvider,
        IMcpClient mcpClient,
        IConversationManager conversationManager,
        IPromptService promptService,
        IStreamingService streamingService,
        ISessionManager sessionManager,
        IContextManager contextManager,
        TokenCounter tokenCounter)
    {
        _logger = logger;
        _config = options.Value;
        _llmProvider = llmProvider;
        _mcpClient = mcpClient;
        _conversationManager = conversationManager;
        _promptService = promptService;
        _streamingService = streamingService;
        _sessionManager = sessionManager;
        _contextManager = contextManager;
        _tokenCounter = tokenCounter;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing AI Agent: {AgentName}", _config.Agent.Name);

        try
        {
            var isLlmAvailable = await _llmProvider.IsAvailableAsync(cancellationToken);
            if (!isLlmAvailable)
            {
                throw new InvalidOperationException("LLM provider is not available");
            }
            _logger.LogInformation("LLM provider is ready");

            await _mcpClient.InitializeAsync(cancellationToken);
            var connectedServers = await _mcpClient.GetConnectedServersAsync();
            _logger.LogInformation("MCP client initialized with {ServerCount} servers: {Servers}", 
                connectedServers.Count, string.Join(", ", connectedServers));

            _logger.LogInformation("Agent initialization completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize agent");
            throw;
        }
    }

    public async Task<AgentResponse> ProcessAsync(string input, string? sessionId = null, CancellationToken cancellationToken = default)
    {
        var conversationId = await _sessionManager.GetOrCreateSessionAsync(sessionId, cancellationToken);
        
        var request = new AgentRequest
        {
            Message = input,
            ConversationId = conversationId
        };

        return await ProcessAsync(request, cancellationToken);
    }

    public async Task<AgentResponse> ProcessAsync(AgentRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing request for conversation {ConversationId}", request.ConversationId);

            await AddUserMessageAsync(request.ConversationId, request.Message, cancellationToken);
            
            var history = await _conversationManager.GetHistoryAsync(request.ConversationId, cancellationToken: cancellationToken);
            var availableTools = await _mcpClient.GetAvailableToolsAsync(cancellationToken);

            // Optimize context for token constraints
            var maxPromptTokens = _config.Llm.MaxTokens - _config.Llm.MaxToolContextTokens;
            var optimizedContext = await _contextManager.OptimizeContextAsync(
                history, availableTools, request.Message, maxPromptTokens, cancellationToken);

            var systemPrompt = BuildSystemPrompt();
            var contextualPrompt = BuildOptimizedPrompt(systemPrompt, request.Message, optimizedContext);
            
            _logger.LogDebug("Context optimization: {Strategy}, Tokens: {Tokens}, Tools: {ToolCount}",
                optimizedContext.OptimizationStrategy, optimizedContext.TokensUsed, optimizedContext.RelevantTools.Count);
            
            var response = await _llmProvider.GenerateResponseAsync(
                contextualPrompt,
                optimizedContext.RecentMessages,
                optimizedContext.RelevantTools,
                cancellationToken);

            var agentResponse = await ProcessLlmResponse(response, request.ConversationId, cancellationToken);
            
            await AddAssistantMessageAsync(request.ConversationId, agentResponse.Message, cancellationToken);

            _logger.LogInformation("Successfully processed request for conversation {ConversationId}", request.ConversationId);
            return agentResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing request for conversation {ConversationId}", request.ConversationId);
            return new AgentResponse
            {
                ConversationId = request.ConversationId,
                IsSuccess = false,
                Error = ex.Message,
                Message = "I encountered an error while processing your request. Please try again."
            };
        }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Shutting down AI Agent");
        await _mcpClient.ShutdownAsync(cancellationToken);
        _logger.LogInformation("Agent shutdown completed");
    }

    private string BuildSystemPrompt()
    {
        try
        {
            return _promptService.GetSystemPromptAsync(_config.Agent.Name, _config.Agent.SystemPrompt, _config.Agent.PromptStyle).Result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build system prompt, using fallback");
            
            // Fallback prompt if service fails
            return $@"{_config.Agent.SystemPrompt}

You are {_config.Agent.Name}, an AI assistant with MCP capabilities.

When you need to use a tool, respond with JSON in this format:
{{""tool_call"": {{""name"": ""tool_name"", ""arguments"": {{""param"": ""value""}}}}}}

Use EXACT tool names and include ALL required parameters.";
        }
    }

    private async Task<AgentResponse> ProcessLlmResponse(string response, string conversationId, CancellationToken cancellationToken)
    {
        return await ProcessLlmResponseWithChaining(response, conversationId, 0, cancellationToken);
    }

    private async Task<AgentResponse> ProcessLlmResponseWithChaining(
        string response, 
        string conversationId, 
        int currentIteration, 
        CancellationToken cancellationToken)
    {
        var agentResponse = new AgentResponse
        {
            ConversationId = conversationId,
            Message = response,
            ToolChainLength = currentIteration
        };

        // Check for max iterations to prevent infinite loops
        if (currentIteration >= _config.Agent.MaxToolChainIterations)
        {
            _logger.LogWarning("Tool chain reached maximum iterations ({MaxIterations}) for conversation {ConversationId}", 
                _config.Agent.MaxToolChainIterations, conversationId);
            agentResponse.ChainTerminated = true;
            agentResponse.Message += "\n\n[Tool chain reached maximum iterations limit]";
            return agentResponse;
        }

        if (!_config.Agent.EnableToolChaining && currentIteration > 0)
        {
            _logger.LogDebug("Tool chaining is disabled, stopping after first tool call");
            agentResponse.ChainTerminated = true;
            return agentResponse;
        }

        if (TryParseToolCall(response, out var toolCall))
        {
            try
            {
                _logger.LogInformation("Executing tool call {Iteration}: {ToolName}", currentIteration + 1, toolCall.Name);
                
                var result = await _mcpClient.CallToolAsync(toolCall.Name, toolCall.Arguments, cancellationToken);
                
                toolCall.Result = result;
                toolCall.IsSuccess = true;
                agentResponse.ToolCalls.Add(toolCall);

                // Add tool call and result to conversation history for context
                await AddToolCallToHistory(conversationId, toolCall, cancellationToken);

                var toolResultPrompt = BuildToolResultPrompt(toolCall, result, currentIteration);
                var history = await _conversationManager.GetHistoryAsync(conversationId, cancellationToken: cancellationToken);
                var availableTools = await _mcpClient.GetAvailableToolsAsync(cancellationToken);
                
                var followupResponse = await _llmProvider.GenerateResponseAsync(
                    toolResultPrompt,
                    history,
                    availableTools,
                    cancellationToken: cancellationToken);

                // Recursively process the followup response for potential chaining
                var chainedResponse = await ProcessLlmResponseWithChaining(
                    followupResponse, 
                    conversationId, 
                    currentIteration + 1, 
                    cancellationToken);

                // Merge responses
                agentResponse.Message = chainedResponse.Message;
                agentResponse.ToolCalls.AddRange(chainedResponse.ToolCalls);
                agentResponse.ToolChainLength = chainedResponse.ToolChainLength;
                agentResponse.ChainTerminated = chainedResponse.ChainTerminated;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing tool call {Iteration}: {ToolName}", currentIteration + 1, toolCall.Name);
                toolCall.IsSuccess = false;
                toolCall.Error = ex.Message;
                agentResponse.ToolCalls.Add(toolCall);
                agentResponse.Message = $"I encountered an error while using the {toolCall.Name} tool: {ex.Message}";
                agentResponse.ChainTerminated = true;
            }
        }

        return agentResponse;
    }

    private string BuildToolResultPrompt(ToolCall toolCall, object? result, int iteration)
    {
        try
        {
            var placeholders = new Dictionary<string, string>
            {
                ["ToolName"] = toolCall.Name,
                ["ToolResult"] = JsonSerializer.Serialize(result)
            };

            // For ReAct style, use the ReAct-specific prompt template
            var promptName = _config.Agent.PromptStyle.ToLower() switch
            {
                "react" => "tool-result-react",
                _ => iteration == 0 ? "tool-result" : "tool-chain"
            };

            if (promptName == "tool-chain")
            {
                placeholders["StepNumber"] = (iteration + 1).ToString();
            }

            return _promptService.GetPromptAsync(promptName, placeholders).Result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build tool result prompt, using fallback");
            
            // Fallback based on style
            if (_config.Agent.PromptStyle.ToLower() == "react")
            {
                return $"**Observation:** {JsonSerializer.Serialize(result)}\n\n**Thought:** Now I need to analyze this result and decide what to do next.";
            }
            else
            {
                return $"Tool '{toolCall.Name}' returned: {JsonSerializer.Serialize(result)}\n\nProvide a response based on this result or use another tool if needed.";
            }
        }
    }

    private async Task AddToolCallToHistory(string conversationId, ToolCall toolCall, CancellationToken cancellationToken)
    {
        var toolMessage = new ConversationMessage
        {
            Role = "tool",
            Content = $"Tool: {toolCall.Name}\nArguments: {JsonSerializer.Serialize(toolCall.Arguments)}\nResult: {JsonSerializer.Serialize(toolCall.Result)}"
        };
        
        await _conversationManager.AddMessageAsync(conversationId, toolMessage, cancellationToken);
    }

    private bool TryParseToolCall(string response, out ToolCall toolCall)
    {
        toolCall = new ToolCall();
        
        try
        {
            // Strategy 1: ReAct pattern - look for "Action Input:" section
            if (response.Contains("Action Input:"))
            {
                var actionInputIndex = response.IndexOf("Action Input:");
                if (actionInputIndex >= 0)
                {
                    var afterActionInput = response.Substring(actionInputIndex + "Action Input:".Length);
                    var jsonStart = afterActionInput.IndexOf('{');
                    
                    if (jsonStart >= 0)
                    {
                        var jsonStartAbsolute = actionInputIndex + "Action Input:".Length + jsonStart;
                        var jsonEnd = response.LastIndexOf('}') + 1;
                        
                        if (jsonEnd > jsonStartAbsolute)
                        {
                            var jsonStr = response.Substring(jsonStartAbsolute, jsonEnd - jsonStartAbsolute);
                            
                            if (TryParseJsonToolCall(jsonStr, toolCall))
                            {
                                _logger.LogDebug("Successfully parsed ReAct tool call: {ToolName}", toolCall.Name);
                                return true;
                            }
                        }
                    }
                }
            }
            
            // Strategy 2: Direct JSON with tool_call (fallback)
            if (response.Contains("tool_call"))
            {
                var jsonStart = response.IndexOf('{');
                var jsonEnd = response.LastIndexOf('}') + 1;
                
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    var jsonStr = response.Substring(jsonStart, jsonEnd - jsonStart);
                    
                    if (TryParseJsonToolCall(jsonStr, toolCall))
                    {
                        _logger.LogDebug("Successfully parsed direct JSON tool call: {ToolName}", toolCall.Name);
                        return true;
                    }
                }
            }
            
            // Strategy 3: Look for common patterns that indicate tool usage intent
            var lowerResponse = response.ToLower();
            if (lowerResponse.Contains("list") && (lowerResponse.Contains("directory") || lowerResponse.Contains("files")))
            {
                toolCall.Name = "list_directory";
                toolCall.Arguments = new Dictionary<string, object> { ["path"] = "." };
                _logger.LogDebug("Inferred tool call from natural language: list_directory");
                return true;
            }
            
            if (lowerResponse.Contains("read") && lowerResponse.Contains("file"))
            {
                // This would need more sophisticated NLP to extract filename
                _logger.LogDebug("Detected file read intent but couldn't extract filename");
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse tool call from response: {Response}", response.Substring(0, Math.Min(100, response.Length)));
        }

        return false;
    }

    private bool TryParseJsonToolCall(string jsonStr, ToolCall toolCall)
    {
        try
        {
            // Clean up common JSON formatting issues
            jsonStr = jsonStr.Replace("'", "\"")  // Fix single quotes
                           .Replace("True", "true")   // Fix Python-style booleans
                           .Replace("False", "false")
                           .Replace("None", "null")
                           .Trim();
            
            var parsed = JsonSerializer.Deserialize<JsonElement>(jsonStr);
            
            if (parsed.TryGetProperty("tool_call", out var toolCallElement))
            {
                if (toolCallElement.TryGetProperty("name", out var nameElement))
                    toolCall.Name = nameElement.GetString() ?? string.Empty;
                
                if (toolCallElement.TryGetProperty("arguments", out var argsElement))
                {
                    toolCall.Arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(argsElement.GetRawText()) ?? new();
                }
                
                return !string.IsNullOrEmpty(toolCall.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse JSON tool call: {Json}", jsonStr.Substring(0, Math.Min(100, jsonStr.Length)));
        }
        
        return false;
    }

    private async Task AddUserMessageAsync(string conversationId, string message, CancellationToken cancellationToken)
    {
        var userMessage = new ConversationMessage
        {
            Role = "user",
            Content = message
        };
        
        await _conversationManager.AddMessageAsync(conversationId, userMessage, cancellationToken);
    }

    private async Task AddAssistantMessageAsync(string conversationId, string message, CancellationToken cancellationToken)
    {
        var assistantMessage = new ConversationMessage
        {
            Role = "assistant",
            Content = message
        };
        
        await _conversationManager.AddMessageAsync(conversationId, assistantMessage, cancellationToken);
    }

    private string BuildOptimizedPrompt(string systemPrompt, string currentMessage, OptimizedContext context)
    {
        var contextBuilder = new StringBuilder();
        contextBuilder.AppendLine(systemPrompt);
        contextBuilder.AppendLine();

        // Add available tools info - prioritize relevant tools
        if (context.RelevantTools.Count > 0)
        {
            contextBuilder.AppendLine("🔧 PRIORITY TOOLS (most relevant for this request):");
            foreach (var tool in context.RelevantTools.Take(5))
            {
                contextBuilder.AppendLine($"• {tool.Name}: {tool.Description ?? "Available tool"}");
            }
            
            if (context.AllTools.Count > context.RelevantTools.Count)
            {
                var otherToolsCount = context.AllTools.Count - context.RelevantTools.Count;
                contextBuilder.AppendLine($"• ... and {otherToolsCount} other tools available");
            }
            contextBuilder.AppendLine();
        }

        // Add conversation context
        if (context.HasSummary && !string.IsNullOrEmpty(context.HistorySummary))
        {
            contextBuilder.AppendLine($"📋 {context.HistorySummary}");
            contextBuilder.AppendLine();
        }

        if (context.RecentMessages.Count > 0)
        {
            contextBuilder.AppendLine("💬 Recent conversation:");
            foreach (var message in context.RecentMessages)
            {
                if (message.Role == "tool")
                {
                    var toolSummary = SummarizeToolCall(message.Content);
                    contextBuilder.AppendLine($"[Tool used: {toolSummary}]");
                }
                else
                {
                    contextBuilder.AppendLine($"{char.ToUpper(message.Role[0])}{message.Role.Substring(1)}: {message.Content}");
                }
            }
            contextBuilder.AppendLine();
        }

        contextBuilder.AppendLine($"User: {currentMessage}");
        
        return contextBuilder.ToString();
    }


    private string SummarizeToolCall(string toolCallContent)
    {
        try
        {
            // Extract tool name from the tool call content
            if (toolCallContent.StartsWith("Tool: "))
            {
                var lines = toolCallContent.Split('\n');
                var toolLine = lines.FirstOrDefault(l => l.StartsWith("Tool: "));
                if (toolLine != null)
                {
                    return toolLine.Substring(6); // Remove "Tool: " prefix
                }
            }
            return "unknown tool";
        }
        catch
        {
            return "tool call";
        }
    }
}