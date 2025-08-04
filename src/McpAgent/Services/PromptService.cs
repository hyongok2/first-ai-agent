using Microsoft.Extensions.Logging;

namespace McpAgent.Services;

public class PromptService : IPromptService
{
    private readonly ILogger<PromptService> _logger;
    private readonly Dictionary<string, string> _promptCache = new();

    public PromptService(ILogger<PromptService> logger)
    {
        _logger = logger;
    }

    public async Task<string> GetSystemPromptAsync(string agentName, string baseSystemPrompt, string promptStyle = "direct")
    {
        var placeholders = new Dictionary<string, string>
        {
            ["AgentName"] = agentName,
            ["BaseSystemPrompt"] = baseSystemPrompt
        };

        var promptName = promptStyle.ToLower() switch
        {
            "react" => "system-prompt-react",
            _ => "system-prompt"
        };

        return await GetPromptAsync(promptName, placeholders);
    }

    public async Task<string> GetPromptAsync(string promptName, Dictionary<string, string>? placeholders = null)
    {
        try
        {
            var cacheKey = placeholders ==null ? $"{promptName}" : $"{promptName}_{string.Join("_", placeholders.Keys )}";
            
            if (!_promptCache.ContainsKey(promptName))
            {
                var promptPath = Path.Combine(AppContext.BaseDirectory, "Prompts", $"{promptName}.txt");
                
                if (!File.Exists(promptPath))
                {
                    _logger.LogError("Prompt file not found: {PromptPath}", promptPath);
                    return GetFallbackPrompt(promptName, placeholders);
                }

                var promptContent = await File.ReadAllTextAsync(promptPath);
                _promptCache[promptName] = promptContent;
                _logger.LogDebug("Loaded prompt template: {PromptName}", promptName);
            }

            var template = _promptCache[promptName];
            
            // Replace placeholders
            if (placeholders != null)
            {
                foreach (var placeholder in placeholders)
                {
                    template = template.Replace($"{{{placeholder.Key}}}", placeholder.Value);
                }
            }

            return template;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load prompt: {PromptName}", promptName);
            return GetFallbackPrompt(promptName, placeholders);
        }
    }

    private string GetFallbackPrompt(string promptName, Dictionary<string, string>? placeholders)
    {
        if (promptName == "system-prompt")
        {
            var agentName = placeholders?.GetValueOrDefault("AgentName", "AI Assistant") ?? "AI Assistant";
            var basePrompt = placeholders?.GetValueOrDefault("BaseSystemPrompt", "") ?? "";
            
            return $@"{basePrompt}

You are {agentName}, an AI assistant with MCP capabilities.

When you need to use a tool, respond with JSON in this format:
{{""tool_call"": {{""name"": ""tool_name"", ""arguments"": {{""param"": ""value""}}}}}}

Use EXACT tool names and include ALL required parameters.";
        }

        return "Fallback prompt content";
    }


}