using McpAgent.Application.Interfaces;
using McpAgent.Presentation.Console;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace McpAgent.Presentation.Hosting;

public class AgentHostService : BackgroundService
{
    private readonly ILogger<AgentHostService> _logger;
    private readonly IAgentService _agentService;
    private readonly InteractiveHostService _interactiveHost;
    private readonly ConsoleUIService _consoleUI;

    public AgentHostService(
        ILogger<AgentHostService> logger,
        IAgentService agentService,
        InteractiveHostService interactiveHost,
        ConsoleUIService consoleUI)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _agentService = agentService ?? throw new ArgumentNullException(nameof(agentService));
        _interactiveHost = interactiveHost ?? throw new ArgumentNullException(nameof(interactiveHost));
        _consoleUI = consoleUI ?? throw new ArgumentNullException(nameof(consoleUI));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Initialize the agent service
            await _agentService.InitializeAsync(stoppingToken);
            
            // Create initial session
            var sessionId = Guid.NewGuid().ToString();
            
            // 시스템 로깅은 파일로만, 사용자 메시지는 콘솔에 직접 출력
            _logger.LogInformation("AI Agent started. Type 'quit' or 'exit' to stop.");
            _logger.LogInformation("Starting interactive session with ID: {SessionId}", sessionId);

            // Display welcome message
            _consoleUI.DisplayWelcomeMessage(sessionId);

            // Start interactive session
            await _interactiveHost.RunInteractiveSessionAsync(sessionId, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in agent host service");
            _consoleUI.DisplayError($"Agent failed to start: {ex.Message}");
        }
        finally
        {
            await _agentService.ShutdownAsync(stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("AgentHostService is stopping...");

        try
        {
            await _agentService.ShutdownAsync(cancellationToken);
            _logger.LogInformation("Agent shutdown completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during agent shutdown");
        }

        await base.StopAsync(cancellationToken);
        _logger.LogInformation("AgentHostService stopped");
    }
}