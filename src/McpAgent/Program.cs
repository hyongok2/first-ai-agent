using McpAgent.Configuration;
using McpAgent.Core;
using McpAgent.Mcp;
using McpAgent.Memory;
using McpAgent.Providers;
using McpAgent.Services;
using McpAgent.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Parse CLI options first
var cliOptions = CliOptions.ParseOptions(args);

var builder = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true);
        config.AddEnvironmentVariables("MCPAGENT_");
    })
    .ConfigureServices((context, services) =>
    {
        // Configure agent with CLI overrides
        services.Configure<AgentConfiguration>(agentConfig =>
        {
            context.Configuration.GetSection("Agent").Bind(agentConfig);
            
            // Apply CLI overrides
            if (!string.IsNullOrEmpty(cliOptions.Model))
            {
                agentConfig.Llm.Model = cliOptions.Model;
            }
            
            if (cliOptions.Temperature != 0.7) // Only override if not default
            {
                agentConfig.Llm.Temperature = cliOptions.Temperature;
            }
            
            if (cliOptions.MaxTokens != 8192) // Only override if not default
            {
                agentConfig.Llm.MaxTokens = cliOptions.MaxTokens;
            }
            
            if (!string.IsNullOrEmpty(cliOptions.SystemPrompt))
            {
                agentConfig.Agent.SystemPrompt = cliOptions.SystemPrompt;
            }
            
            if (!string.IsNullOrEmpty(cliOptions.PromptStyle))
            {
                agentConfig.Agent.PromptStyle = cliOptions.PromptStyle;
            }
        });

        // Store CLI options for services that need them
        services.AddSingleton(cliOptions);

        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.AddHttpClient();

        services.AddSingleton<ILlmProvider, OllamaProvider>();
        services.AddSingleton<IMcpClient, McpClient>();
        services.AddSingleton<IConversationManager, InMemoryConversationManager>();
        services.AddSingleton<IPromptService, PromptService>();
        services.AddSingleton<IStreamingService, ConsoleStreamingService>();
        services.AddSingleton<ISessionManager, SessionManager>();
        services.AddSingleton<IHealthCheckService, HealthCheckService>();
        services.AddSingleton<IAgent, Agent>();

        services.AddHostedService<AgentHostedService>();
    })
    .UseConsoleLifetime();

try
{
    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"Application failed to start: {ex.Message}");
    Environment.Exit(1);
}