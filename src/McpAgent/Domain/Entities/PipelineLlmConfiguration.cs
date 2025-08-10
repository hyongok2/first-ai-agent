namespace McpAgent.Domain.Entities;

/// <summary>
/// 파이프라인별 LLM 설정을 관리하는 클래스
/// </summary>
public class PipelineLlmConfiguration
{
    /// <summary>
    /// 각 파이프라인 타입별 LLM 모델 설정
    /// </summary>
    public Dictionary<PipelineType, PipelineLlmSettings> PipelineSettings { get; set; } = new();

    /// <summary>
    /// 기본 LLM 설정 (파이프라인별 설정이 없을 때 사용)
    /// </summary>
    public PipelineLlmSettings DefaultSettings { get; set; } = new();

    /// <summary>
    /// 특정 파이프라인에 대한 LLM 설정을 가져옵니다.
    /// 파이프라인별 설정이 없으면 기본 설정을 반환합니다.
    /// </summary>
    /// <param name="pipelineType">파이프라인 타입</param>
    /// <returns>해당 파이프라인의 LLM 설정</returns>
    public PipelineLlmSettings GetSettingsForPipeline(PipelineType pipelineType)
    {
        return PipelineSettings.TryGetValue(pipelineType, out var settings) 
            ? settings 
            : DefaultSettings;
    }

    /// <summary>
    /// 설정의 유효성을 검증합니다.
    /// </summary>
    public void Validate()
    {
        if (DefaultSettings == null)
            throw new ArgumentException("DefaultSettings는 필수입니다.");

        DefaultSettings.Validate();

        foreach (var (pipelineType, settings) in PipelineSettings)
        {
            try
            {
                settings.Validate();
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"Pipeline {pipelineType} 설정이 유효하지 않습니다: {ex.Message}", ex);
            }
        }
    }
}

/// <summary>
/// 개별 파이프라인의 LLM 설정
/// </summary>
public class PipelineLlmSettings
{
    /// <summary>
    /// 사용할 LLM 모델명 (예: "qwen3-32b", "gpt-4")
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// 온도 설정 (0.0 ~ 2.0)
    /// </summary>
    public double Temperature { get; set; } = 0.7;

    /// <summary>
    /// 최대 토큰 수
    /// </summary>
    public int MaxTokens { get; set; } = 8192;

    /// <summary>
    /// LLM 제공자 (예: "ollama", "openai")
    /// </summary>
    public string Provider { get; set; } = "ollama";

    /// <summary>
    /// API 엔드포인트 (선택사항)
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// API 키 (선택사항)
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// 설정의 유효성을 검증합니다.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrEmpty(Model))
            throw new ArgumentException("Model은 필수입니다.");

        if (string.IsNullOrEmpty(Provider))
            throw new ArgumentException("Provider는 필수입니다.");

        if (Temperature < 0.0 || Temperature > 2.0)
            throw new ArgumentException("Temperature는 0.0과 2.0 사이여야 합니다.");

        if (MaxTokens <= 0)
            throw new ArgumentException("MaxTokens는 양수여야 합니다.");
    }
}