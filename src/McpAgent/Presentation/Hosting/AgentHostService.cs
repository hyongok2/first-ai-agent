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

            // 초기화 성공 메시지를 콘솔에 직접 출력
            System.Console.WriteLine("🎉 McpAgent 초기화 완료!");
            System.Console.WriteLine("✅ MCP 서버 연결됨, LLM 서비스 준비됨");
            System.Console.WriteLine("📝 이제부터 자유롭게 대화하실 수 있습니다.");
            System.Console.WriteLine();

            // Display welcome message
            _consoleUI.DisplayWelcomeMessage(sessionId);
            _consoleUI.DisplaySuccessMessage("✅ All systems ready");

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