using McpAgent.Application.Interfaces;
using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;
using McpAgent.Domain.Services;
using McpAgent.Presentation.Console;
using Microsoft.Extensions.Logging;

namespace McpAgent.Application.Agent;

public class AgentService : IAgentService
{
    private readonly ILogger<AgentService> _logger;
    private readonly AgentOrchestrator _orchestrator;
    private readonly IMcpClientAdapter _mcpClient;
    private readonly ILlmProvider _llmProvider;

    private readonly ConsoleUIService _consoleUI;

    public AgentService(ILogger<AgentService> logger, AgentOrchestrator orchestrator, IMcpClientAdapter mcpClient, ILlmProvider llmProvider, ConsoleUIService consoleUI)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _mcpClient = mcpClient ?? throw new ArgumentNullException(nameof(mcpClient));
        _llmProvider = llmProvider ?? throw new ArgumentNullException(nameof(llmProvider));
        _consoleUI = consoleUI ?? throw new ArgumentNullException(nameof(consoleUI));
    }

    public async Task<AgentResponse> ProcessRequestAsync(AgentRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Processing agent request for conversation {ConversationId}", request.ConversationId);

        try
        {
            var response = await _orchestrator.ProcessRequestAsync(request, cancellationToken);

            _logger.LogInformation("Agent request processed successfully for conversation {ConversationId}", request.ConversationId);
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process agent request for conversation {ConversationId}", request.ConversationId);
            return AgentResponse.Failure($"Processing failed: {ex.Message}", request.ConversationId);
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing Agent Service with health checks...");

        // MCP와 LLM 초기화를 병렬로 실행
        var mcpTask = InitializeMcpAsync(cancellationToken);
        var llmTask = TestLlmConnectionAsync(cancellationToken);

        var results = await Task.WhenAll(mcpTask, llmTask);

        var mcpHealthy = results[0];
        var llmHealthy = results[1];

        _logger.LogInformation("Initialization completed - MCP: {McpStatus}, LLM: {LlmStatus}",
            mcpHealthy ? "Healthy" : "Degraded",
            llmHealthy ? "Healthy" : "Degraded");

        if (!mcpHealthy && !llmHealthy)
        {
            _logger.LogWarning("Both MCP and LLM services failed initialization. Running in severely degraded mode.");
            _consoleUI.DisplayError("MCP서버 및 LLM 서비스 초기화에 실패하였습니다. 비정상 모드로 시작됩니다.");
        }
        else if (!mcpHealthy)
        {
            _logger.LogWarning("MCP services failed initialization. Tool functionality will be limited.");
            _consoleUI.DisplayError("MCP 서버 초기화에 실패하였습니다. 도구 사용이 제한됩니다.");
        }
        else if (!llmHealthy)
        {
            _logger.LogWarning("LLM service failed initialization. AI responses will be unavailable.");
            _consoleUI.DisplayError("LLM 서비스 초기화에 실패하였습니다. AI 응답이 제한됩니다.");
        }
        else
        {
            _logger.LogInformation("All services initialized successfully. System ready for operation.");
            _consoleUI.DisplaySuccessMessage("모든 서비스가 정상적으로 초기화 되었습니다. 시스템이 정상 작동합니다.");
        }
    }

    private async Task<bool> InitializeMcpAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Initializing MCP client adapter...");
            await _mcpClient.InitializeAsync(cancellationToken);

            // MCP 초기화가 성공했으면 연결된 서버 수만 확인 (이미 연결 테스트 완료)
            var connectedServers = await _mcpClient.GetConnectedServersAsync();

            if (connectedServers.Count > 0)
            {
                _logger.LogInformation("MCP initialization completed - {ServerCount} servers connected", connectedServers.Count);
                return true;
            }
            else
            {
                _logger.LogWarning("MCP initialized but no servers connected");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP initialization failed");
            return false;
        }
    }

    private async Task<bool> TestLlmConnectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Testing LLM service connectivity...");

            // 간단한 테스트 요청으로 실제 응답 확인
            var testResponse = await _llmProvider.GenerateResponseAsync(
                "System health check. Please respond with 'OK'.", cancellationToken);

            if (!string.IsNullOrEmpty(testResponse))
            {
                _logger.LogInformation("LLM health check passed - received response of {Length} characters", testResponse.Length);
                return true;
            }
            else
            {
                _logger.LogWarning("LLM health check failed - empty response received");
                return false;
            }

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM service test failed");
            return false;
        }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Shutting down Agent Service");

        try
        {
            _logger.LogInformation("Shutting down MCP client adapter...");
            await _mcpClient.ShutdownAsync(cancellationToken);
            _logger.LogInformation("MCP client adapter shut down successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to shutdown MCP client adapter");
        }
    }
}