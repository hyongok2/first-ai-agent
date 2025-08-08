using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace McpAgent.Presentation.Console;

public class ConsoleUIService
{
    private readonly ILogger<ConsoleUIService> _logger;
    private const int SessionIdDisplayLength = 8;

    public ConsoleUIService(ILogger<ConsoleUIService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void DisplayWelcomeMessage(string sessionId)
    {
        System.Console.WriteLine($"🤖 McpAgent 초기화 완료! Session: {sessionId[..SessionIdDisplayLength]}");
        System.Console.WriteLine("📝 이제부터 자유롭게 대화하실 수 있습니다.");
    }

    public void DisplaySuccessMessage(string message)
    {
        System.Console.ForegroundColor = ConsoleColor.Green;
        System.Console.WriteLine(message);
        System.Console.ResetColor();
    }

    public void DisplayWarning(string message)
    {
        System.Console.ForegroundColor = ConsoleColor.Yellow;
        System.Console.WriteLine(message);
        System.Console.ResetColor();
    }

    public void DisplayError(string message)
    {
        System.Console.ForegroundColor = ConsoleColor.Red;
        System.Console.WriteLine($"Error: {message}");
        System.Console.ResetColor();
    }

    public void DisplayProcess(string message)
    {
        System.Console.ForegroundColor = ConsoleColor.DarkGray;
        System.Console.WriteLine($"\n진행 단계: {message}");
        System.Console.ResetColor();
    }

    public void ShowHelp()
    {
        const string helpMessage = @"
Available commands:
- help: Show this help message
- tools: Show available MCP tools
- status: Show system health status
- new/reset: Start a new conversation session
- quit/exit: Stop the agent
- Any other message: Chat with the AI agent

The agent has access to MCP tools and can help you with various tasks.
Your current session preserves conversation history for context.";

        System.Console.WriteLine(helpMessage);
    }

    public void ShowTools(IReadOnlyList<ToolDefinition> tools, int serverCount)
    {
        const string sectionHeader = "\\n🔧 Available MCP Tools:";
        const string separator = "=======================================================";

        System.Console.WriteLine(sectionHeader);
        System.Console.WriteLine(separator);

        if (tools.Count == 0)
        {
            System.Console.WriteLine("No tools are currently available.");
            if (serverCount == 0)
            {
                System.Console.WriteLine("No MCP servers are connected.");
            }
        }
        else
        {
            System.Console.WriteLine($"Found {tools.Count} tools from {serverCount} server(s):\\n");

            foreach (var tool in tools)
            {
                System.Console.WriteLine($"• {tool.Name}");
                if (!string.IsNullOrEmpty(tool.Description))
                {
                    System.Console.WriteLine($"  {tool.Description}");
                }
            }
        }

        System.Console.WriteLine(separator);
    }

    public void DisplayAgentResponse(AgentResponse response)
    {
        if (response.IsSuccess)
        {
            System.Console.WriteLine(response.Message);
            DisplayToolChainInfo(response);
        }
        else
        {
            DisplayError(response.Error);
        }
    }

    private void DisplayToolChainInfo(AgentResponse response)
    {
        if (!response.ToolCalls.Any()) return;

        var toolNames = response.ToolCalls.Select(t => t.Name).ToList();
        System.Console.ForegroundColor = ConsoleColor.DarkGray;
        System.Console.WriteLine($"\\n[Tool Chain: {response.ToolChainLength + 1} steps, Used {response.ToolCalls.Count} tool(s): {string.Join(" → ", toolNames)}]");

        if (response.ChainTerminated)
        {
            System.Console.WriteLine("[Chain terminated due to limits or completion]");
        }
        System.Console.ResetColor();
    }

    public void DisplayNewSessionMessage(string sessionId)
    {
        System.Console.WriteLine($"🔄 Started new session: {sessionId[..SessionIdDisplayLength]}...");
    }
}