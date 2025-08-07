using McpAgent.Application.Interfaces;
using McpAgent.Configuration;
using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;
using McpAgent.Shared.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Ollama;

namespace McpAgent.Infrastructure.LLM;

public class OllamaLlmService : ILlmService
{
    private readonly ILogger<OllamaLlmService> _logger;
    private readonly LlmConfiguration _config;
    private readonly Kernel _kernel;
    private readonly IRequestResponseLogger _requestResponseLogger;

    public OllamaLlmService(ILogger<OllamaLlmService> logger, IOptions<LlmConfiguration> options, IRequestResponseLogger requestResponseLogger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = options.Value ?? throw new ArgumentNullException(nameof(options));
        _requestResponseLogger = requestResponseLogger ?? throw new ArgumentNullException(nameof(requestResponseLogger));

        _kernel = Kernel.CreateBuilder()
            .AddOllamaChatCompletion(
                modelId: _config.Model,
                endpoint: new Uri(_config.Endpoint))
            .Build();
    }

    public async Task<string> GenerateResponseAsync(
        string prompt, 
        IReadOnlyList<ConversationMessage> history,
        CancellationToken cancellationToken = default)
    {
        return await GenerateResponseAsync(prompt, history, Array.Empty<ToolDefinition>(), cancellationToken);
    }

    public async Task<string> GenerateResponseAsync(
        string prompt,
        IReadOnlyList<ConversationMessage> history,
        IReadOnlyList<ToolDefinition> availableTools,
        CancellationToken cancellationToken = default)
    {
        var chatHistory = BuildChatHistory(prompt, history);
        
        try
        {
            var response = await _kernel.InvokePromptAsync(chatHistory, cancellationToken: cancellationToken);
            var result = response.GetValue<string>() ?? string.Empty;

            // 요청 완료 직후 파싱 전에 로깅 (사용자가 강조한 부분)
            _ = Task.Run(() => _requestResponseLogger.LogLlmRequestResponseAsync(
                _config.Model, 
                "GenerateResponse", 
                chatHistory, 
                result, 
                CancellationToken.None));

            return result;
        }
        catch (Exception ex)
        {
            // 에러 발생시에도 로깅
            _ = Task.Run(() => _requestResponseLogger.LogLlmRequestResponseAsync(
                _config.Model, 
                "GenerateResponse-Error", 
                chatHistory, 
                $"Error: {ex.Message}", 
                CancellationToken.None));

            _logger.LogError(ex, "Failed to generate response from Ollama");
            throw new AgentException("LLM_ERROR", "LLM generation failed", ex);
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _kernel.InvokePromptAsync("Hello", cancellationToken: cancellationToken);
            return !string.IsNullOrEmpty(response.GetValue<string>());
        }
        catch
        {
            return false;
        }
    }

    private string BuildChatHistory(string currentPrompt, IReadOnlyList<ConversationMessage> history)
    {
        var chatHistory = string.Join("\\n", 
            history.Select(m => $"{m.Role}: {m.Content}"));
        
        return $"{chatHistory}\\nUser: {currentPrompt}\\nAssistant:";
    }
}

