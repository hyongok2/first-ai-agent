using System.Text;
using System.Text.Json;
using McpAgent.Configuration;
using McpAgent.Mcp;
using McpAgent.Memory;
using McpAgent.Models;
using McpAgent.Providers;
using McpAgent.Services;
using McpAgent.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace McpAgent.Core;

public class Agent : IAgent
{
    private readonly ILogger<Agent> _logger;
    private readonly AgentConfiguration _config;
    private readonly ConversationOrchestrator _orchestrator;
    private readonly ISessionManager _sessionManager;
    private readonly IMcpClient _mcpClient;
    private readonly ISystemContextProvider _systemContextProvider;

    public Agent(
        ILogger<Agent> logger,
        IOptions<AgentConfiguration> options,
        ConversationOrchestrator orchestrator,
        ISessionManager sessionManager,
        IMcpClient mcpClient,
        ISystemContextProvider systemContextProvider)
    {
        _logger = logger;
        _config = options.Value;
        _orchestrator = orchestrator;
        _sessionManager = sessionManager;
        _mcpClient = mcpClient;
        _systemContextProvider = systemContextProvider;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing Enhanced AI Agent: {AgentName} v{Version}", 
            _config.Agent.Name, "2.0.0");

        try
        {
            // MCP 클라이언트 초기화
            await _mcpClient.InitializeAsync(cancellationToken);
            var connectedServers = await _mcpClient.GetConnectedServersAsync();
            _logger.LogInformation("MCP client initialized with {ServerCount} servers: {Servers}", 
                connectedServers.Count, string.Join(", ", connectedServers));

            // 시스템 컨텍스트 초기화
            await _systemContextProvider.RefreshDynamicDataAsync();
            var systemContext = await _systemContextProvider.GetCurrentContextAsync(cancellationToken);
            
            // 사용 가능한 도구 목록을 시스템 컨텍스트에 업데이트
            var availableTools = await _mcpClient.GetAvailableToolsAsync(cancellationToken);
            systemContext.AvailableTools = availableTools.Select(t => t.Name).ToList();

            _logger.LogInformation("Enhanced Agent initialization completed successfully");
            _logger.LogInformation("System Context: {OS}, {TimeZone}, {ToolCount} tools available", 
                systemContext.OperatingSystem, 
                systemContext.TimeZone.StandardName,
                systemContext.AvailableTools.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize enhanced agent");
            throw;
        }
    }

    public async Task<AgentResponse> ProcessAsync(string input, string? sessionId = null, CancellationToken cancellationToken = default)
    {
        var conversationId = await _sessionManager.GetOrCreateSessionAsync(sessionId, cancellationToken);
        
        var request = new AgentRequest
        {
            Message = input,
            ConversationId = conversationId
        };

        return await ProcessAsync(request, cancellationToken);
    }

    public async Task<AgentResponse> ProcessAsync(AgentRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing enhanced multi-phase request for conversation {ConversationId}", 
                request.ConversationId);

            // 새로운 다단계 처리 시스템 사용
            var response = await _orchestrator.ProcessAsync(request.Message, request.ConversationId);
            
            _logger.LogInformation("Enhanced processing completed for conversation {ConversationId} with {PhaseCount} phases", 
                request.ConversationId, 
                response.Metadata.GetValueOrDefault("total_phases", 0));
                
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in enhanced processing for conversation {ConversationId}", 
                request.ConversationId);
                
            return new AgentResponse
            {
                ConversationId = request.ConversationId,
                IsSuccess = false,
                Error = ex.Message,
                Message = "처리 중 오류가 발생했습니다. 다시 시도해 주세요.",
                Metadata = new Dictionary<string, object>
                {
                    ["error_type"] = "enhanced_processing_error",
                    ["timestamp"] = DateTime.UtcNow
                }
            };
        }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Shutting down Enhanced AI Agent");
        await _mcpClient.ShutdownAsync(cancellationToken);
        _logger.LogInformation("Enhanced Agent shutdown completed");
    }
}