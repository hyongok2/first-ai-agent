using McpAgent.Application.Interfaces;
using McpAgent.Domain.Interfaces;
using McpAgent.Presentation.Console;
using Microsoft.Extensions.Logging;

namespace McpAgent.Presentation.Console;

public class CommandHandlerService
{
    private readonly IToolExecutor _toolExecutor;

    private readonly ConsoleUIService _consoleUIService;

    public CommandHandlerService(ConsoleUIService consoleUIService, IToolExecutor toolExecutor)
    {
        _consoleUIService = consoleUIService ?? throw new ArgumentNullException(nameof(consoleUIService));
        _toolExecutor = toolExecutor ?? throw new ArgumentNullException(nameof(toolExecutor));
    }

    public async Task<CommandResult> HandleCommandAsync(string command, string? currentSessionId, CancellationToken cancellationToken = default)
    {

        return command.ToLowerInvariant() switch
        {
            "quit" or "exit" => CommandResult.Exit,
            "help" => _consoleUIService.ShowHelp() ,
            "tools" => _consoleUIService.ShowTools(await _toolExecutor.GetAvailableToolsAsync()), 
            "new" or "reset" => await HandleNewSessionCommand(cancellationToken),
            _ => CommandResult.NotHandled
        };
    }

    private Task<CommandResult> HandleNewSessionCommand(CancellationToken cancellationToken)
    {
        var newSessionId = Guid.NewGuid().ToString();
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