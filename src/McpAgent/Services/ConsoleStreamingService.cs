using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace McpAgent.Services;

public class ConsoleStreamingService : IStreamingService
{
    private readonly ILogger<ConsoleStreamingService> _logger;
    private readonly object _consoleLock = new();

    public ConsoleStreamingService(ILogger<ConsoleStreamingService> logger)
    {
        _logger = logger;
    }

    public async Task StreamResponseAsync(string response, CancellationToken cancellationToken = default)
    {
        lock (_consoleLock)
        {
            // Simulate streaming by printing character by character with small delay
            foreach (char c in response)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                Console.Write(c);
                Thread.Sleep(10); // Small delay for streaming effect
            }
            Console.WriteLine();
        }

        await Task.CompletedTask;
    }

    public Task StreamToolCallAsync(string toolName, Dictionary<string, object> arguments, CancellationToken cancellationToken = default)
    {
        lock (_consoleLock)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"\n🔧 Calling tool: {toolName}");

            if (arguments.Any())
            {
                Console.WriteLine($"   Arguments: {JsonSerializer.Serialize(arguments, new JsonSerializerOptions { WriteIndented = false })}");
            }

            Console.Write("   Executing");
            for (int i = 0; i < 3; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                Task.Delay(300, cancellationToken);
                Console.Write(".");
            }
            Console.WriteLine();
            Console.ResetColor();
        }

        return Task.CompletedTask;
    }

    public async Task StreamToolResultAsync(string toolName, object? result, bool isSuccess, CancellationToken cancellationToken = default)
    {
        lock (_consoleLock)
        {
            if (isSuccess)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✅ Tool '{toolName}' completed successfully");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ Tool '{toolName}' failed");
            }

            Console.ResetColor();
        }

        await Task.CompletedTask;
    }
}