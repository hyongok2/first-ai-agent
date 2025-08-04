namespace McpAgent.Services;

public interface IStreamingService
{
    Task StreamResponseAsync(string response, CancellationToken cancellationToken = default);
    Task StreamToolCallAsync(string toolName, Dictionary<string, object> arguments, CancellationToken cancellationToken = default);
    Task StreamToolResultAsync(string toolName, object? result, bool isSuccess, CancellationToken cancellationToken = default);
}