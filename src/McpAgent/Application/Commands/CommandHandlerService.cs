using McpAgent.Application.Interfaces;
using McpAgent.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace McpAgent.Application.Commands;

public class CommandHandlerService
{
    private readonly ILogger<CommandHandlerService> _logger;
    private readonly IToolExecutor _toolExecutor;

    public CommandHandlerService(ILogger<CommandHandlerService> logger, IToolExecutor toolExecutor)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _toolExecutor = toolExecutor ?? throw new ArgumentNullException(nameof(toolExecutor));
    }

    public async Task<CommandResult> HandleCommandAsync(string command, string? currentSessionId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Handling command: {Command}", command);

        return command.ToLowerInvariant() switch
        {
            "quit" or "exit" => CommandResult.Exit,
            "help" => CommandResult.Handled,
            "tools" => await HandleToolsCommand(cancellationToken),
            "status" => await HandleStatusCommand(cancellationToken),
            "new" or "reset" => await HandleNewSessionCommand(cancellationToken),
            _ => CommandResult.NotHandled
        };
    }

    private async Task<CommandResult> HandleToolsCommand(CancellationToken cancellationToken)
    {
        try
        {
            var tools = await _toolExecutor.GetAvailableToolsAsync(cancellationToken);
            _logger.LogInformation("Retrieved {ToolCount} available tools", tools.Count);
            return CommandResult.Handled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve tools");
            return CommandResult.Handled;
        }
    }

    private Task<CommandResult> HandleStatusCommand(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Status command handled");
        return Task.FromResult(CommandResult.Handled);
    }

    private Task<CommandResult> HandleNewSessionCommand(CancellationToken cancellationToken)
    {
        var newSessionId = Guid.NewGuid().ToString();
        _logger.LogInformation("Created new session: {SessionId}", newSessionId);
        return Task.FromResult(CommandResult.NewSession(newSessionId));
    }
}

public readonly struct CommandResult
{
    public bool ShouldExit { get; }
    public bool WasHandled { get; }
    public string? NewSessionId { get; }

    private CommandResult(bool shouldExit, bool wasHandled, string? newSessionId = null)
    {
        ShouldExit = shouldExit;
        WasHandled = wasHandled;
        NewSessionId = newSessionId;
    }

    public static CommandResult Exit => new(shouldExit: true, wasHandled: true);
    public static CommandResult Handled => new(shouldExit: false, wasHandled: true);
    public static CommandResult NotHandled => new(shouldExit: false, wasHandled: false);
    public static CommandResult NewSession(string sessionId) => new(shouldExit: false, wasHandled: true, newSessionId: sessionId);
}