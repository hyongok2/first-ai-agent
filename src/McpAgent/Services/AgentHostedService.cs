using McpAgent.Core;
using McpAgent.Common;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace McpAgent.Services;

public class AgentHostedService : BackgroundService
{
    private readonly ILogger<AgentHostedService> _logger;
    private readonly IAgent _agent;
    private readonly IStreamingService _streamingService;

    public AgentHostedService(ILogger<AgentHostedService> logger, IAgent agent, IStreamingService streamingService)
    {
        _logger = logger;
        _agent = agent;
        _streamingService = streamingService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _agent.InitializeAsync(stoppingToken);
            
            _logger.LogInformation("AI Agent started. Type 'quit' or 'exit' to stop.");
            _logger.LogInformation("Starting interactive session...");

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

    private async Task RunInteractiveSessionAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                Console.Write("\nYou: ");
                var input = Console.ReadLine();

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

                Console.Write("Assistant: ");
                
                try
                {
                    var response = await RetryHelper.RetryAsync(
                        () => _agent.ProcessAsync(input, cancellationToken),
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
- quit/exit: Stop the agent
- Any other message: Chat with the AI agent

The agent has access to MCP tools and can help you with various tasks.
        ");
    }

    private async Task ShowAvailableToolsAsync(CancellationToken cancellationToken)
    {
        try
        {
            Console.WriteLine("\n🔧 Available MCP Tools:");
            Console.WriteLine("=======================================================");
            
            // This would need to be implemented in the agent
            // For now, show a placeholder
            Console.WriteLine("📁 File Operations:");
            Console.WriteLine("  • list_directory - List files and folders");
            Console.WriteLine("  • read_file - Read file contents");
            Console.WriteLine("  • write_file - Create or modify files");
            
            Console.WriteLine("\n🌐 Web Operations:");
            Console.WriteLine("  • fetch_url - Retrieve web content");
            Console.WriteLine("  • send_request - Make HTTP requests");
            
            Console.WriteLine("\n⚙️ System Operations:");
            Console.WriteLine("  • run_command - Execute system commands");
            Console.WriteLine("  • get_system_info - Get system information");
            
            Console.WriteLine("=======================================================");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error showing available tools");
            Console.WriteLine("Unable to retrieve tool information at this time.");
        }
    }
}