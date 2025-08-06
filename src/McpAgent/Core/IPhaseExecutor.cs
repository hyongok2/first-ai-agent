using McpAgent.Models;

namespace McpAgent.Core;

public interface IPhaseExecutor
{
    int PhaseNumber { get; }
    Task<PhaseResult> ExecuteAsync(ConversationState state, string userInput, CancellationToken cancellationToken = default);
}

public interface IPhaseExecutorFactory
{
    IPhaseExecutor GetExecutor(int phaseNumber);
}