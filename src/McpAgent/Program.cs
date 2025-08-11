using McpAgent.Application.Agent;
using McpAgent.Application.Interfaces;
using McpAgent.Application.Services;
using McpAgent.Configuration;
using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;
using McpAgent.Domain.Services;
using McpAgent.Infrastructure.CLI;
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
    // CLI 옵션 파싱 및 환경 변수 설정
    CliOptions.ParseAndSetEnvironmentVariables(args);
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
            services.Configure<LlmConfiguration>(context.Configuration.GetSection("Agent:Llm"));
            services.Configure<PipelineLlmConfiguration>(context.Configuration.GetSection("Agent:PipelineLlm"));
            services.Configure<AgentSettings>(context.Configuration.GetSection("Agent:Agent"));
            
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

            // Core Agent Orchestrator (keeping both for gradual migration)
            services.AddSingleton<AgentOrchestrator>();
            services.AddSingleton<OptimizedAgentOrchestrator>();

            // Multi-Step Pipeline Services (using working fallback implementations)
            services.AddSingleton<IInputRefinementService, InputRefinementService>();
            services.AddSingleton<ICapabilitySelectionService, CapabilitySelectionService>();
            services.AddSingleton<IConversationSummaryService, ConversationSummaryService>();
            services.AddSingleton<IParameterGenerationService, ParameterGenerationService>();
            services.AddSingleton<IResponseGenerationService, ResponseGenerationService>();
            
            // New Integrated Analysis Service for optimized pipeline
            services.AddSingleton<IIntegratedAnalysisService, IntegratedAnalysisService>();

            // Legacy Application Services (keeping for compatibility)
            services.AddSingleton<IAgentService, AgentService>();
            services.AddSingleton<CommandHandlerService>();

            // Infrastructure Services
            services.AddSingleton<ILlmProvider, OllamaProvider>(); // Backward compatibility
            services.AddSingleton<ILlmProviderFactory, LlmProviderFactory>(); // New pipeline-aware factory
            services.AddSingleton<IPromptService, PromptService>();
            services.AddSingleton<IToolExecutor, McpToolExecutor>();
            services.AddSingleton<IConversationRepository, InMemoryConversationRepository>();


            // HTTP Client Factory for MCP HTTP clients
            services.AddHttpClient();

            // MCP Client Factory for creating appropriate MCP clients
            services.AddSingleton<IMcpClientFactory, McpClientFactory>();

            // MCP Client Adapter - HTTP 전용
            services.AddSingleton<IMcpClientAdapter>(provider =>
            {
                var factory = provider.GetRequiredService<IMcpClientFactory>();
                var config = context.Configuration.GetSection("Agent:Mcp").Get<McpConfiguration>();
                
                if (config?.Enabled != true || config.Servers == null || !config.Servers.Any())
                {
                    throw new InvalidOperationException("MCP configuration is required. Please configure at least one HTTP MCP server.");
                }
                
                // 서버가 하나면 단일 클라이언트 사용
                if (config.Servers.Count == 1)
                {
                    return factory.CreateClient(config.Servers.First());
                }
                
                // 여러 서버가 있으면 Composite 패턴 사용
                var compositeLogger = provider.GetRequiredService<ILogger<CompositeMcpClientAdapter>>();
                return new CompositeMcpClientAdapter(compositeLogger, factory, config);
            });

            // Presentation Services
            services.AddSingleton<IDisplayResult, ConsoleUIService>();
            services.AddSingleton<IDisplayProcess, ConsoleUIService>();
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