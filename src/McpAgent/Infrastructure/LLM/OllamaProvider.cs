using McpAgent.Configuration;
using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;
using McpAgent.Shared.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Ollama;

namespace McpAgent.Infrastructure.LLM;

/// <summary>
/// Ollama LLM 공급자 구현 - 다단계 파이프라인용
/// </summary>
public class OllamaProvider : ILlmProvider
{
    private readonly ILogger<OllamaProvider> _logger;
    private readonly LlmConfiguration _config;
    private readonly Kernel _kernel;
    private readonly string _llmModel;

    public OllamaProvider(ILogger<OllamaProvider> logger, IOptions<LlmConfiguration> options)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = options.Value ?? throw new ArgumentNullException(nameof(options));
        _llmModel = _config.Model;
        _kernel = Kernel.CreateBuilder()
            .AddOllamaChatCompletion(
                modelId: _config.Model,
                endpoint: new Uri(_config.Endpoint))
            .Build();
    }

    public async Task<string> GenerateResponseAsync(string prompt, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Generating response for prompt: {Prompt}", prompt.Substring(0, Math.Min(100, prompt.Length)));

            var response = await _kernel.InvokePromptAsync(prompt, cancellationToken: cancellationToken);
            var result = response.GetValue<string>() ?? string.Empty;

            _logger.LogDebug("Generated response length: {Length}", result.Length);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate response from Ollama");
            throw new AgentException("LLM_ERROR", "LLM generation failed", ex);
        }
    }

    public string GetLlmModel()
    {
        return _llmModel;
    }
}
