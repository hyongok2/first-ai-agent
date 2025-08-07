using McpAgent.Domain.Entities;

namespace McpAgent.Domain.Interfaces;

public interface IToolExecutor
{
    Task<ToolCall> ExecuteAsync(string toolName, Dictionary<string, object> arguments, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ToolDefinition>> GetAvailableToolsAsync(CancellationToken cancellationToken = default);
    Task<bool> IsToolAvailableAsync(string toolName, CancellationToken cancellationToken = default);
}