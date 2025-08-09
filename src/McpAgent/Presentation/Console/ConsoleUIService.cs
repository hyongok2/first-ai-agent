using System.Text;
using McpAgent.Application.Commands;
using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace McpAgent.Presentation.Console;

public class ConsoleUIService
{
    private const int SessionIdDisplayLength = 8;

    public ConsoleUIService()
    {
        System.Console.OutputEncoding = Encoding.UTF8;
        System.Console.InputEncoding = Encoding.UTF8;
    }

    public void DisplayWelcomeMessage(string sessionId)
    {
        System.Console.WriteLine($"🤖 McpAgent 초기화 완료! Session: {sessionId[..SessionIdDisplayLength]}");
        System.Console.WriteLine("📝 이제부터 자유롭게 대화하실 수 있습니다.");
    }

    public void DisplaySuccessMessage(string message)
    {
        System.Console.ForegroundColor = ConsoleColor.Green;
        System.Console.WriteLine("🚀 성공: " + message);
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
        System.Console.WriteLine($"🛠️ 에러: {message}");
        System.Console.ResetColor();
    }

    public void DisplayProcess(string message)
    {
        System.Console.ForegroundColor = ConsoleColor.DarkGray;
        System.Console.WriteLine($"\n⏳ 진행 단계: {message}");
        System.Console.ResetColor();
    }

    public CommandResult ShowHelp()
    {
        const string helpMessage = @"
Available commands:
- help: Show this help message
- tools: Show available MCP tools
- new/reset: Start a new conversation session
- quit/exit: Stop the agent
- Any other message: Chat with the AI agent

The agent has access to MCP tools and can help you with various tasks.
Your current session preserves conversation history for context.";

        System.Console.WriteLine(helpMessage);

        return CommandResult.Handled;
    }

    public CommandResult ShowTools(IReadOnlyList<ToolDefinition> tools)
    {
        const string sectionHeader = "\n🔧 Available MCP Tools:";
        const string separator = "=======================================================";

        System.Console.WriteLine(sectionHeader);
        System.Console.WriteLine(separator);

        if (tools.Count == 0)
        {
            System.Console.WriteLine("No tools are currently available.");
            return CommandResult.Handled;
        }

        System.Console.WriteLine($"Found {tools.Count} tools:\n");

        foreach (var tool in tools)
        {
            System.Console.WriteLine($"• {tool.Name}");
            if (!string.IsNullOrEmpty(tool.Description))
            {
                System.Console.WriteLine($"  {tool.Description}");
            }
        }

        System.Console.WriteLine(separator);
        return CommandResult.Handled;
    }

    public void DisplayAgentResponse(AgentResponse response)
    {
        if (response.IsSuccess)
        {
            System.Console.WriteLine(response.Message);
            return;
        }
        DisplayError(response.Error);
    }

    public void DisplayNewSessionMessage(string sessionId)
    {
        System.Console.WriteLine($"🔄 Started new session: {sessionId[..SessionIdDisplayLength]}...");
    }
}