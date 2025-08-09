using McpAgent.Application.Interfaces;
using McpAgent.Application.Commands;
using McpAgent.Domain.Entities;
using McpAgent.Presentation.Console;
using Microsoft.Extensions.Logging;

namespace McpAgent.Presentation.Hosting;

public class InteractiveHostService
{
    private const string UserPrompt = "\n💬 질문을 입력하세요: ";
    private const string AssistantPrompt = "🤖 에이전트가 응답합니다: ";

    private readonly ILogger<InteractiveHostService> _logger;
    private readonly IAgentService _agentService;
    private readonly CommandHandlerService _commandHandler;
    private readonly ConsoleUIService _consoleUI;

    public InteractiveHostService(
        ILogger<InteractiveHostService> logger,
        IAgentService agentService,
        CommandHandlerService commandHandler,
        ConsoleUIService consoleUI)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _agentService = agentService ?? throw new ArgumentNullException(nameof(agentService));
        _commandHandler = commandHandler ?? throw new ArgumentNullException(nameof(commandHandler));
        _consoleUI = consoleUI ?? throw new ArgumentNullException(nameof(consoleUI));
    }

    public async Task RunInteractiveSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        var currentSessionId = sessionId;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var input = await GetUserInputAsync(cancellationToken);
                if (input == null) break; // EOF or cancellation

                if (string.IsNullOrWhiteSpace(input)) continue;

                var commandResult = await _commandHandler.HandleCommandAsync(input, currentSessionId, cancellationToken);

                if (commandResult.ShouldExit) break;
                if (commandResult.WasHandled && commandResult.NewSessionId == null) continue;
                if (commandResult.NewSessionId != null) // Update session if a new one was created
                {
                    currentSessionId = commandResult.NewSessionId;
                    _consoleUI.DisplayNewSessionMessage(currentSessionId);
                    continue;
                }

                await ProcessUserMessageAsync(input, currentSessionId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in interactive session");
                System.Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }

        System.Console.WriteLine("\n✨ 에이전트를 종료합니다. 안녕히 가세요, 또 만나요!!👋");
    }

    private async Task<string?> GetUserInputAsync(CancellationToken cancellationToken)
    {
        System.Console.Write(UserPrompt);

        try
        {
            var inputTask = Task.Run(() => System.Console.ReadLine(), cancellationToken);
            var input = await inputTask;

            if (input == null)
            {
                _logger.LogWarning("Console input stream closed, terminating session");
                return null;
            }

            return input;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private async Task ProcessUserMessageAsync(string input, string sessionId, CancellationToken cancellationToken)
    {
        System.Console.Write(AssistantPrompt);

        try
        {
            var request = new AgentRequest(input, sessionId);
            var response = await _agentService.ProcessRequestAsync(request, cancellationToken);

            _consoleUI.DisplayAgentResponse(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process user input");
            _consoleUI.DisplayError("I'm experiencing technical difficulties. Please try again.");
        }
    }
}