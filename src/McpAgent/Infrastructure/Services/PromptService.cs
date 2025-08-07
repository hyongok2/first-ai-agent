using McpAgent.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace McpAgent.Infrastructure.Services;

public class PromptService : IPromptService
{
    private readonly ILogger<PromptService> _logger;
    private readonly string _promptsBasePath;
    private readonly Dictionary<string, string> _promptCache;

    public PromptService(ILogger<PromptService> logger)
    {
        _logger = logger;
        _promptsBasePath = Path.Combine(AppContext.BaseDirectory, "Resources", "Prompts");
        _promptCache = new Dictionary<string, string>();
    }

    public async Task<string> GetPromptAsync(string promptName, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check cache first
            if (_promptCache.TryGetValue(promptName, out var cachedPrompt))
            {
                return cachedPrompt;
            }

            // Load from file
            var promptFilePath = Path.Combine(_promptsBasePath, $"{promptName}.txt");
            
            if (!File.Exists(promptFilePath))
            {
                _logger.LogError("Prompt file not found: {PromptFile}", promptFilePath);
                throw new FileNotFoundException($"Prompt file not found: {promptName}.txt");
            }

            var promptContent = await File.ReadAllTextAsync(promptFilePath, cancellationToken);
            
            // Cache the prompt for future use
            _promptCache[promptName] = promptContent;
            
            _logger.LogDebug("Loaded prompt: {PromptName}", promptName);
            
            return promptContent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load prompt: {PromptName}", promptName);
            throw;
        }
    }

    public async Task<Dictionary<string, string>> GetAllPromptsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var prompts = new Dictionary<string, string>();
            
            if (!Directory.Exists(_promptsBasePath))
            {
                _logger.LogWarning("Prompts directory not found: {PromptsPath}", _promptsBasePath);
                return prompts;
            }

            var promptFiles = Directory.GetFiles(_promptsBasePath, "*.txt");
            
            foreach (var file in promptFiles)
            {
                var promptName = Path.GetFileNameWithoutExtension(file);
                var content = await File.ReadAllTextAsync(file, cancellationToken);
                prompts[promptName] = content;
                
                // Also cache it
                _promptCache[promptName] = content;
            }

            _logger.LogInformation("Loaded {Count} prompts", prompts.Count);
            
            return prompts;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load all prompts");
            throw;
        }
    }

    public void ClearCache()
    {
        _promptCache.Clear();
        _logger.LogDebug("Prompt cache cleared");
    }

    public bool IsPromptCached(string promptName)
    {
        return _promptCache.ContainsKey(promptName);
    }
}