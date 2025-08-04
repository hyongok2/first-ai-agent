using McpAgent.Configuration;
using McpAgent.Models;
using McpAgent.Utils;
using McpAgent.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Ollama;

namespace McpAgent.Providers;

public class OllamaProvider : ILlmProvider
{
    private readonly ILogger<OllamaProvider> _logger;
    private readonly LlmConfiguration _config;
    private readonly Kernel _kernel;
    
    private TokenCounter _tokenCounter;

    public OllamaProvider(ILogger<OllamaProvider> logger, IOptions<AgentConfiguration> options,
        TokenCounter tokenCounter)
    {
        _logger = logger;
        _config = options.Value.Llm;

        _tokenCounter = tokenCounter;

        _kernel = Kernel.CreateBuilder()
            .AddOllamaChatCompletion(
                modelId: _config.Model,
                endpoint: new Uri(_config.Endpoint))
            .Build();
    }

    public async Task<string> GenerateResponseAsync(
        string prompt, 
        List<ConversationMessage> history,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var fullPrompt = BuildPrompt(prompt, history);
            var response = await _kernel.InvokePromptAsync(fullPrompt, cancellationToken: cancellationToken);
            return response.GetValue<string>() ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating response from Ollama");
            throw;
        }
    }

    public async Task<string> GenerateResponseAsync(
        string prompt,
        List<ConversationMessage> history,
        List<ToolDefinition> availableTools,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var toolsPrompt = BuildToolsPrompt(availableTools);
            var fullPrompt = BuildPrompt($"{toolsPrompt}\n\n{prompt}", history);
            
            var response = await _kernel.InvokePromptAsync(fullPrompt, cancellationToken: cancellationToken);
            return response.GetValue<string>() ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating response with tools from Ollama");
            throw;
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _kernel.InvokePromptAsync("test", cancellationToken: cancellationToken);
            return response != null;
        }
        catch
        {
            return false;
        }
    }

    private string BuildPrompt(string userPrompt, List<ConversationMessage> history)
    {
        var prompt = new System.Text.StringBuilder();
        
        // Trim history to fit within token limits
        var historyTexts = history.Select(m => $"{m.Role}: {m.Content}").ToList();
        var trimmedHistory = _tokenCounter.TrimToTokenLimit(historyTexts, _config.MaxHistoryTokens);
        
        foreach (var historyText in trimmedHistory)
        {
            prompt.AppendLine(historyText);
        }
        
        prompt.AppendLine($"user: {userPrompt}");
        prompt.AppendLine("assistant:");
        
        var finalPrompt = prompt.ToString();
        var estimatedTokens = _tokenCounter.EstimateTokens(finalPrompt);
        
        _logger.LogDebug("Built prompt with estimated {TokenCount} tokens", estimatedTokens);
        
        if (estimatedTokens > _config.MaxTokens * 0.8) // 80% threshold warning
        {
            _logger.LogWarning("Prompt is approaching token limit: {TokenCount}/{MaxTokens}", 
                estimatedTokens, _config.MaxTokens);
        }
        
        return finalPrompt;
    }

    private string BuildToolsPrompt(List<ToolDefinition> tools)
    {
        if (!tools.Any()) return string.Empty;

        var toolsDescription = new System.Text.StringBuilder();
        toolsDescription.AppendLine("**AVAILABLE TOOLS - USE EXACT NAMES:**");
        toolsDescription.AppendLine();
        
        foreach (var tool in tools)
        {
            toolsDescription.AppendLine($"🔧 **{tool.Name}**");
            toolsDescription.AppendLine($"   Description: {tool.Description}");
            
            if (tool.Parameters.Any())
            {
                toolsDescription.AppendLine("   Parameters:");
                foreach (var param in tool.Parameters)
                {
                    var required = param.Value.Required ? "**REQUIRED**" : "optional";
                    var defaultVal = param.Value.DefaultValue != null ? $" (default: {param.Value.DefaultValue})" : "";
                    toolsDescription.AppendLine($"   - {param.Key}: {param.Value.Type} ({required}){defaultVal}");
                    toolsDescription.AppendLine($"     → {param.Value.Description}");
                }
            }
            else
            {
                toolsDescription.AppendLine("   Parameters: None");
            }
            
            toolsDescription.AppendLine($"   Example: {{\"tool_call\": {{\"name\": \"{tool.Name}\", \"arguments\": {{}}}}");
            toolsDescription.AppendLine();
        }
        
        toolsDescription.AppendLine("**TOOL USAGE RULES:**");
        toolsDescription.AppendLine("1. Copy tool names EXACTLY as shown above");
        toolsDescription.AppendLine("2. Include ALL required parameters");
        toolsDescription.AppendLine("3. Use proper JSON syntax - no extra text");
        toolsDescription.AppendLine("4. Only one tool call per response");
        toolsDescription.AppendLine();
        
        return toolsDescription.ToString();
    }
}