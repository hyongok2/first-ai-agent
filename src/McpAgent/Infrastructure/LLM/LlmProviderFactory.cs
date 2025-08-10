using McpAgent.Configuration;
using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace McpAgent.Infrastructure.LLM;

/// <summary>
/// 파이프라인별 LLM Provider를 생성하는 팩토리 구현체
/// </summary>
public class LlmProviderFactory : ILlmProviderFactory
{
    private readonly ILogger<LlmProviderFactory> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly PipelineLlmConfiguration _pipelineLlmConfig;
    private readonly LlmConfiguration _defaultLlmConfig;
    private readonly Dictionary<string, ILlmProvider> _providerCache;

    public LlmProviderFactory(
        ILogger<LlmProviderFactory> logger,
        ILoggerFactory loggerFactory,
        IOptions<PipelineLlmConfiguration> pipelineLlmConfig,
        IOptions<LlmConfiguration> defaultLlmConfig)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _pipelineLlmConfig = pipelineLlmConfig?.Value ?? throw new ArgumentNullException(nameof(pipelineLlmConfig));
        _defaultLlmConfig = defaultLlmConfig?.Value ?? throw new ArgumentNullException(nameof(defaultLlmConfig));
        _providerCache = new Dictionary<string, ILlmProvider>();

        // 설정 유효성 검증
        _pipelineLlmConfig.Validate();
        _defaultLlmConfig.Validate();
    }

    public ILlmProvider CreateForPipeline(PipelineType pipelineType)
    {
        var settings = _pipelineLlmConfig.GetSettingsForPipeline(pipelineType);
        var cacheKey = $"{pipelineType}_{settings.Model}_{settings.Provider}";

        if (_providerCache.TryGetValue(cacheKey, out var cachedProvider))
        {
            _logger.LogDebug("Returning cached LLM provider for pipeline {PipelineType} with model {Model}", 
                pipelineType, settings.Model);
            return cachedProvider;
        }

        var provider = CreateProviderFromSettings(settings);
        _providerCache[cacheKey] = provider;

        _logger.LogInformation("Created new LLM provider for pipeline {PipelineType} with model {Model} (Provider: {Provider})", 
            pipelineType, settings.Model, settings.Provider);

        return provider;
    }

    public ILlmProvider CreateDefault()
    {
        const string cacheKey = "default";

        if (_providerCache.TryGetValue(cacheKey, out var cachedProvider))
        {
            _logger.LogDebug("Returning cached default LLM provider with model {Model}", _defaultLlmConfig.Model);
            return cachedProvider;
        }

        var provider = CreateProviderFromLlmConfig(_defaultLlmConfig);
        _providerCache[cacheKey] = provider;

        _logger.LogInformation("Created default LLM provider with model {Model} (Provider: {Provider})", 
            _defaultLlmConfig.Model, _defaultLlmConfig.Provider);

        return provider;
    }

    public ILlmProvider CreateWithSettings(PipelineLlmSettings settings)
    {
        settings.Validate();

        var cacheKey = $"custom_{settings.Model}_{settings.Provider}";

        if (_providerCache.TryGetValue(cacheKey, out var cachedProvider))
        {
            _logger.LogDebug("Returning cached custom LLM provider with model {Model}", settings.Model);
            return cachedProvider;
        }

        var provider = CreateProviderFromSettings(settings);
        _providerCache[cacheKey] = provider;

        _logger.LogInformation("Created custom LLM provider with model {Model} (Provider: {Provider})", 
            settings.Model, settings.Provider);

        return provider;
    }

    private ILlmProvider CreateProviderFromSettings(PipelineLlmSettings settings)
    {
        return settings.Provider.ToLower() switch
        {
            "ollama" => CreateOllamaProvider(settings),
            "openai" => CreateOpenAIProvider(settings),
            "anthropic" => CreateAnthropicProvider(settings),
            _ => throw new NotSupportedException($"LLM Provider '{settings.Provider}'는 지원되지 않습니다.")
        };
    }

    private ILlmProvider CreateProviderFromLlmConfig(LlmConfiguration config)
    {
        return config.Provider.ToLower() switch
        {
            "ollama" => CreateOllamaProviderFromConfig(config),
            "openai" => CreateOpenAIProviderFromConfig(config),
            "anthropic" => CreateAnthropicProviderFromConfig(config),
            _ => throw new NotSupportedException($"LLM Provider '{config.Provider}'는 지원되지 않습니다.")
        };
    }

    private ILlmProvider CreateOllamaProvider(PipelineLlmSettings settings)
    {
        var ollamaConfig = new LlmConfiguration
        {
            Provider = "ollama",
            Model = settings.Model,
            Temperature = settings.Temperature,
            MaxTokens = settings.MaxTokens,
            Endpoint = settings.Endpoint ?? "http://localhost:11434"
        };

        return new OllamaProvider(_loggerFactory.CreateLogger<OllamaProvider>(), Options.Create(ollamaConfig));
    }

    private ILlmProvider CreateOllamaProviderFromConfig(LlmConfiguration config)
    {
        return new OllamaProvider(_loggerFactory.CreateLogger<OllamaProvider>(), Options.Create(config));
    }

    private ILlmProvider CreateOpenAIProvider(PipelineLlmSettings settings)
    {
        // OpenAI Provider 구현이 있다면 여기서 생성
        throw new NotImplementedException("OpenAI Provider는 아직 구현되지 않았습니다.");
    }

    private ILlmProvider CreateOpenAIProviderFromConfig(LlmConfiguration config)
    {
        // OpenAI Provider 구현이 있다면 여기서 생성
        throw new NotImplementedException("OpenAI Provider는 아직 구현되지 않았습니다.");
    }

    private ILlmProvider CreateAnthropicProvider(PipelineLlmSettings settings)
    {
        // Anthropic Provider 구현이 있다면 여기서 생성
        throw new NotImplementedException("Anthropic Provider는 아직 구현되지 않았습니다.");
    }

    private ILlmProvider CreateAnthropicProviderFromConfig(LlmConfiguration config)
    {
        // Anthropic Provider 구현이 있다면 여기서 생성
        throw new NotImplementedException("Anthropic Provider는 아직 구현되지 않았습니다.");
    }

    /// <summary>
    /// 캐시된 Provider들을 정리합니다.
    /// </summary>
    public void ClearCache()
    {
        _providerCache.Clear();
        _logger.LogInformation("LLM provider cache cleared");
    }
}