using McpAgent.Domain.Entities;

namespace McpAgent.Application.Interfaces;

public interface IAgentService
{
    Task<AgentResponse> ProcessRequestAsync(AgentRequest request, CancellationToken cancellationToken = default);
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}