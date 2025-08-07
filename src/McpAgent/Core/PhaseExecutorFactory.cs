using McpAgent.Core.PhaseExecutors;
using McpAgent.Mcp;
using McpAgent.Providers;
using McpAgent.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace McpAgent.Core;

public class PhaseExecutorFactory : IPhaseExecutorFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<int, Func<IPhaseExecutor>> _executorFactories;
    
    public PhaseExecutorFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _executorFactories = new Dictionary<int, Func<IPhaseExecutor>>
        {
            [1] = () => new IntentAnalysisExecutor(
                _serviceProvider.GetRequiredService<ILogger<IntentAnalysisExecutor>>(),
                _serviceProvider.GetRequiredService<ILlmProvider>(),
                _serviceProvider.GetRequiredService<ISystemContextProvider>(),
                _serviceProvider.GetRequiredService<IDebugFileLogger>()),
                
            [2] = () => new FunctionSelectionExecutor(
                _serviceProvider.GetRequiredService<ILogger<FunctionSelectionExecutor>>(),
                _serviceProvider.GetRequiredService<ILlmProvider>(),
                _serviceProvider.GetRequiredService<ISystemContextProvider>(),
                _serviceProvider.GetRequiredService<IMcpClient>(),
                _serviceProvider.GetRequiredService<IDebugFileLogger>()),
                
            [3] = () => new ParameterGenerationExecutor(
                _serviceProvider.GetRequiredService<ILogger<ParameterGenerationExecutor>>(),
                _serviceProvider.GetRequiredService<ILlmProvider>(),
                _serviceProvider.GetRequiredService<ISystemContextProvider>(),
                _serviceProvider.GetRequiredService<IMcpClient>(),
                _serviceProvider.GetRequiredService<IDebugFileLogger>()),
                
            [4] = () => new ToolExecutionExecutor(
                _serviceProvider.GetRequiredService<ILogger<ToolExecutionExecutor>>(),
                _serviceProvider.GetRequiredService<IMcpClient>()),
                
            [5] = () => new ResponseSynthesisExecutor(
                _serviceProvider.GetRequiredService<ILogger<ResponseSynthesisExecutor>>(),
                _serviceProvider.GetRequiredService<ILlmProvider>(),
                _serviceProvider.GetRequiredService<ISystemContextProvider>(),
                _serviceProvider.GetRequiredService<IDebugFileLogger>(),
                _serviceProvider.GetRequiredService<ITokenCalculationService>())
        };
    }
    
    public IPhaseExecutor GetExecutor(int phaseNumber)
    {
        if (_executorFactories.TryGetValue(phaseNumber, out var factory))
        {
            return factory();
        }
        
        throw new ArgumentException($"No executor found for phase {phaseNumber}");
    }
}