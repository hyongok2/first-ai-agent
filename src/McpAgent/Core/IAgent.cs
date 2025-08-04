using McpAgent.Models;

namespace McpAgent.Core;

public interface IAgent
{
    Task<AgentResponse> ProcessAsync(string input, CancellationToken cancellationToken = default);
    Task<AgentResponse> ProcessAsync(AgentRequest request, CancellationToken cancellationToken = default);
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}