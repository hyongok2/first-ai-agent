using McpAgent.Core;
using McpAgent.Common;
using McpAgent.Mcp;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace McpAgent.Services;

public class AgentHostedService : BackgroundService
{
    private readonly ILogger<AgentHostedService> _logger;
    private readonly IAgent _agent;
    private readonly IStreamingService _streamingService;
    private readonly ISessionManager _sessionManager;
    private readonly IMcpClient _mcpClient;
    private readonly IHealthCheckService _healthCheckService;
    private string? _currentSessionId;

    public AgentHostedService(
        ILogger<AgentHostedService> logger, 
        IAgent agent, 
        IStreamingService streamingService,
        ISessionManager sessionManager,
        IMcpClient mcpClient,
        IHealthCheckService healthCheckService)
    {
        _logger = logger;
        _agent = agent;
        _streamingService = streamingService;
        _sessionManager = sessionManager;
        _mcpClient = mcpClient;
        _healthCheckService = healthCheckService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _agent.InitializeAsync(stoppingToken);
            
            // Perform health check
            var healthCheck = await _healthCheckService.CheckOverallHealthAsync(stoppingToken);
            if (!healthCheck.IsHealthy)
            {
                _logger.LogWarning("System health check failed: {Message}", healthCheck.Message);
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"⚠️  Warning: {healthCheck.Message}");
                Console.ResetColor();
            }
            else
            {
                _logger.LogInformation("System health check passed");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✅ All systems ready");
                Console.ResetColor();
            }
            
            // Create initial session
            _currentSessionId = await _sessionManager.GetOrCreateSessionAsync(cancellationToken: stoppingToken);
            
            _logger.LogInformation("AI Agent started. Type 'quit' or 'exit' to stop.");
            _logger.LogInformation("Starting interactive session with ID: {SessionId}", _currentSessionId);
            
            Console.WriteLine($"\n🤖 McpAgent ready! Session: {_currentSessionId[..8]}...");
            Console.WriteLine("Type 'help' for commands or start chatting!");

            await RunInteractiveSessionAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in agent hosted service");
        }
        finally
        {
            await _agent.ShutdownAsync(stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("AgentHostedService is stopping...");
        
        try
        {
            // Ensure agent is properly shut down
            await _agent.ShutdownAsync(cancellationToken);
            _logger.LogInformation("Agent shutdown completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during agent shutdown");
        }
        
        await base.StopAsync(cancellationToken);
        _logger.LogInformation("AgentHostedService stopped");
    }

    private async Task RunInteractiveSessionAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                Console.Write("\nYou: ");
                
                // Read user input with cancellation token support
                string? input;
                try
                {
                    var inputTask = Task.Run(() => Console.ReadLine(), cancellationToken);
                    input = await inputTask;
                    
                    // Check for null input which indicates EOF or stream closure
                    if (input == null)
                    {
                        _logger.LogWarning("Console input stream closed, terminating session");
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    // Application is shutting down
                    break;
                }

                if (string.IsNullOrWhiteSpace(input))
                    continue;

                if (input.Equals("quit", StringComparison.OrdinalIgnoreCase) || 
                    input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (input.Equals("help", StringComparison.OrdinalIgnoreCase))
                {
                    ShowHelp();
                    continue;
                }

                if (input.Equals("tools", StringComparison.OrdinalIgnoreCase))
                {
                    await ShowAvailableToolsAsync(cancellationToken);
                    continue;
                }
                
                if (input.Equals("status", StringComparison.OrdinalIgnoreCase))
                {
                    await ShowSystemStatusAsync(cancellationToken);
                    continue;
                }
                
                if (input.Equals("new", StringComparison.OrdinalIgnoreCase) ||
                    input.Equals("reset", StringComparison.OrdinalIgnoreCase))
                {
                    _currentSessionId = await _sessionManager.GetOrCreateSessionAsync(cancellationToken: cancellationToken);
                    Console.WriteLine($"🔄 Started new session: {_currentSessionId[..8]}...");
                    continue;
                }

                Console.Write("Assistant: ");
                
                try
                {
                    var response = await RetryHelper.RetryAsync(
                        () => _agent.ProcessAsync(input, _currentSessionId, cancellationToken),
                        maxAttempts: 2,
                        logger: _logger,
                        cancellationToken: cancellationToken);

                    if (response.IsSuccess)
                    {
                        // Stream the response for better UX
                        await _streamingService.StreamResponseAsync(response.Message, cancellationToken);
                        
                        if (response.ToolCalls.Any())
                        {
                            var toolNames = response.ToolCalls.Select(t => t.Name).ToList();
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.WriteLine($"\n[Tool Chain: {response.ToolChainLength + 1} steps, Used {response.ToolCalls.Count} tool(s): {string.Join(" → ", toolNames)}]");
                            
                            if (response.ChainTerminated)
                            {
                                Console.WriteLine("[Chain terminated due to limits or completion]");
                            }
                            Console.ResetColor();
                        }
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"Error: {response.Error}");
                        Console.ResetColor();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process user input after retries");
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("I'm experiencing technical difficulties. Please try again.");
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in interactive session");
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }

        Console.WriteLine("Goodbye!");
    }

    private void ShowHelp()
    {
        Console.WriteLine(@"
Available commands:
- help: Show this help message
- tools: Show available MCP tools
- status: Show system health status
- new/reset: Start a new conversation session
- quit/exit: Stop the agent
- Any other message: Chat with the AI agent

The agent has access to MCP tools and can help you with various tasks.
Your current session preserves conversation history for context.
        ");
    }

    private async Task ShowAvailableToolsAsync(CancellationToken cancellationToken)
    {
        try
        {
            Console.WriteLine("\n🔧 Available MCP Tools:");
            Console.WriteLine("=======================================================");
            
            var tools = await _mcpClient.GetAvailableToolsAsync(cancellationToken);
            var connectedServers = await _mcpClient.GetConnectedServersAsync();
            
            if (tools.Count == 0)
            {
                Console.WriteLine("No tools are currently available.");
                if (connectedServers.Count == 0)
                {
                    Console.WriteLine("No MCP servers are connected.");
                }
            }
            else
            {
                Console.WriteLine($"Found {tools.Count} tools from {connectedServers.Count} server(s):\n");
                
                foreach (var tool in tools)
                {
                    Console.WriteLine($"• {tool.Name}");
                    if (!string.IsNullOrEmpty(tool.Description))
                    {
                        Console.WriteLine($"  {tool.Description}");
                    }
                }
            }
            
            Console.WriteLine("=======================================================");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error showing available tools");
            Console.WriteLine("Unable to retrieve tool information at this time.");
        }
    }
    
    private async Task ShowSystemStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            Console.WriteLine("\n📊 System Status:");
            Console.WriteLine("=======================================================");
            
            var healthCheck = await _healthCheckService.CheckOverallHealthAsync(cancellationToken);
            
            if (healthCheck.IsHealthy)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✅ Overall Status: Healthy");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("❌ Overall Status: Unhealthy");
            }
            Console.ResetColor();
            
            if (healthCheck.Details.TryGetValue("llm", out var llmHealth) && llmHealth is HealthCheckResult llm)
            {
                Console.WriteLine($"\n🧠 LLM Provider: {(llm.IsHealthy ? "✅" : "❌")} {llm.Message}");
            }
            
            if (healthCheck.Details.TryGetValue("mcp", out var mcpHealth) && mcpHealth is HealthCheckResult mcp)
            {
                Console.WriteLine($"🔌 MCP Servers: {(mcp.IsHealthy ? "✅" : "❌")} {mcp.Message}");
            }
            
            var activeSessions = await _sessionManager.GetActiveSessionsAsync(cancellationToken);
            Console.WriteLine($"💬 Active Sessions: {activeSessions.Count}");
            Console.WriteLine($"🆔 Current Session: {_currentSessionId?[..8]}...");
            
            Console.WriteLine("=======================================================");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error showing system status");
            Console.WriteLine("Unable to retrieve system status at this time.");
        }
    }
}