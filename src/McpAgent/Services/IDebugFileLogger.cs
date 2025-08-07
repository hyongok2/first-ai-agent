namespace McpAgent.Services;

public interface IDebugFileLogger
{
    Task LogPromptAndResponseAsync(string request, string response, string category = "prompt");
    Task LogMcpRequestAndResponseAsync(string request, string response, string category = "mcp");
}