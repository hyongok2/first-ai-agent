namespace McpAgent.Services;

public interface IPromptService
{
    Task<string> GetSystemPromptAsync(string agentName, string baseSystemPrompt, string promptStyle = "direct");
    Task<string> GetPromptAsync(string promptName, Dictionary<string, string>? placeholders = null);
}