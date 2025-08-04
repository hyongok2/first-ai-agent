using McpAgent.Models;

namespace McpAgent.Providers;

public interface ILlmProvider
{
    Task<string> GenerateResponseAsync(
        string prompt, 
        List<ConversationMessage> history,
        CancellationToken cancellationToken = default);
    
    Task<string> GenerateResponseAsync(
        string prompt,
        List<ConversationMessage> history,
        List<ToolDefinition> availableTools,
        CancellationToken cancellationToken = default);
    
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}

public class ToolDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, ParameterDefinition> Parameters { get; set; } = new();
    public string Category { get; set; } = "general";
    public string Version { get; set; } = "1.0.0";
    public List<string> Tags { get; set; } = new();
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public string? Documentation { get; set; }
    public List<ToolExample> Examples { get; set; } = new();
}

public class ToolExample
{
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, object> Arguments { get; set; } = new();
    public string? ExpectedResult { get; set; }
}

public class ParameterDefinition
{
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Required { get; set; } = false;
    public object? DefaultValue { get; set; }
}