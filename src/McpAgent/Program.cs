using McpAgent.Application.Agent;
using McpAgent.Application.Commands;
using McpAgent.Application.Conversation;
using McpAgent.Application.Interfaces;
using McpAgent.Application.Services;
using McpAgent.Configuration;
using McpAgent.Domain.Interfaces;
using McpAgent.Infrastructure.LLM;
using McpAgent.Infrastructure.Logging;
using McpAgent.Infrastructure.MCP;
using McpAgent.Infrastructure.Services;
using McpAgent.Infrastructure.Storage;
using McpAgent.Presentation.Console;
using McpAgent.Presentation.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

try
{
    var builder = Host.CreateDefaultBuilder(args)
        .ConfigureAppConfiguration((context, config) =>
        {
            // 실행 파일 위치 기준으로 Resources 경로 설정
            var baseDirectory = AppContext.BaseDirectory;
            var configPath = Path.Combine(baseDirectory, "Resources", "Configuration", "appsettings.json");
            var envConfigPath = Path.Combine(baseDirectory, "Resources", "Configuration", $"appsettings.{context.HostingEnvironment.EnvironmentName}.json");
            
            config.AddJsonFile(configPath, optional: false, reloadOnChange: true);
            config.AddJsonFile(envConfigPath, optional: true);
            config.AddEnvironmentVariables("MCPAGENT_");
        })
        .ConfigureLogging(logging =>
        {
            // 모든 기본 로깅을 제거하고 파일 로깅만 유지
            logging.ClearProviders();
        })
        .ConfigureServices((context, services) =>
        {
            // Configuration
            services.Configure<AgentConfiguration>(context.Configuration.GetSection("Agent"));
            services.Configure<McpAgent.Configuration.LlmConfiguration>(context.Configuration.GetSection("Agent:Llm"));
            
            // Logging (파일 로깅만, 콘솔 로깅 제거)
            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                
                // 파일 로깅만 사용 (콘솔 로깅 제거)
                builder.AddProvider(new FileLoggerProvider());
                
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // 요청/응답 파일 로거
            services.AddSingleton<IRequestResponseLogger, FileRequestResponseLogger>();

            // Core Agent Orchestrator (using new multi-step pipeline version)
            services.AddSingleton<McpAgent.Domain.Services.AgentOrchestrator>();

            // Multi-Step Pipeline Services (using working fallback implementations)
            services.AddSingleton<IInputRefinementService, InputRefinementService>();
            services.AddSingleton<ICapabilitySelectionService, CapabilitySelectionService>();
            services.AddSingleton<IConversationSummaryService, ConversationSummaryService>();
            services.AddSingleton<IParameterGenerationService, ParameterGenerationService>();
            services.AddSingleton<IResponseGenerationService, ResponseGenerationService>();

            // Legacy Application Services (keeping for compatibility)
            services.AddSingleton<IAgentService, AgentService>();
            services.AddSingleton<ConversationService>();
            services.AddSingleton<CommandHandlerService>();

            // Infrastructure Services
            services.AddSingleton<ILlmService, OllamaLlmService>();
            services.AddSingleton<ILlmProvider, OllamaProvider>(); // New interface for multi-step pipeline
            services.AddSingleton<IPromptService, PromptService>();
            services.AddSingleton<IToolExecutor, McpToolExecutor>();
            services.AddSingleton<IConversationRepository, InMemoryConversationRepository>();

            // MCP Client Adapter (using proper MCP protocol implementation)
            services.AddSingleton<IMcpClientAdapter, ProperMcpClientAdapter>();

            // Presentation Services
            services.AddSingleton<ConsoleUIService>();
            services.AddSingleton<InteractiveHostService>();

            // Hosting
            services.AddHostedService<AgentHostService>();
        })
        .UseConsoleLifetime();

    var host = builder.Build();
    await host.RunAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"Application failed to start: {ex.Message}");
    Environment.Exit(1);
}