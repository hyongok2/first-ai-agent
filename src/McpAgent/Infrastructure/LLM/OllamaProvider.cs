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

    public async IAsyncEnumerable<string> GenerateStreamingResponseAsync(string prompt, CancellationToken cancellationToken = default)
    {
        // For now, provide a simple non-streaming implementation
        // In a real implementation, this would use streaming capabilities
        var result = await GenerateResponseAsync(prompt, cancellationToken);

        // Split the result into chunks to simulate streaming
        const int chunkSize = 50;
        for (int i = 0; i < result.Length; i += chunkSize)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            var chunk = result.Substring(i, Math.Min(chunkSize, result.Length - i));
            yield return chunk;

            // Small delay to simulate streaming
            await Task.Delay(10, cancellationToken);
        }
    }

    /// <summary>
    /// LLM이 사용 가능한지 확인합니다.
    /// </summary>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>사용 가능 여부</returns>
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _kernel.InvokePromptAsync("Hello", cancellationToken: cancellationToken);
            return !string.IsNullOrEmpty(response.GetValue<string>());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ollama availability check failed");
            return false;
        }
    }
    
    public string GetLlmModel()
    {
        return _llmModel;
    }
}
