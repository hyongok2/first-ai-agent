namespace McpAgent.Configuration;

public class AgentConfiguration
{
    public LlmConfiguration Llm { get; set; } = new();
    public McpConfiguration Mcp { get; set; } = new();
    public AgentSettings Agent { get; set; } = new();
}

public class AgentSettings
{
    /// <summary>
    /// 최적화된 파이프라인 사용 여부 (기본값: true)
    /// true: 통합 분석으로 LLM 호출 최소화, false: 기존 파이프라인 사용
    /// </summary>
    public bool UseOptimizedPipeline { get; set; } = true;
    
    /// <summary>
    /// 최대 대화 이력 길이
    /// </summary>
    public int MaxHistoryLength { get; set; } = 50;
    
    /// <summary>
    /// 최대 툴 체인 반복 횟수
    /// </summary>
    public int MaxToolChainIterations { get; set; } = 5;
    
    /// <summary>
    /// 도구 체이닝 사용 여부
    /// </summary>
    public bool EnableToolChaining { get; set; } = true;
}

public class LlmConfiguration
{
    public string Provider { get; set; } = "ollama";
    public string Endpoint { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "llama3";
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 8192;
    
    public void Validate()
    {
        if (string.IsNullOrEmpty(Provider))
            throw new ArgumentException("Provider is required");
        
        if (string.IsNullOrEmpty(Model))
            throw new ArgumentException("Model is required");
            
        if (MaxTokens <= 0)
            throw new ArgumentException("MaxTokens must be positive");
            
        if (Temperature < 0.0 || Temperature > 2.0)
            throw new ArgumentException("Temperature must be between 0.0 and 2.0");
    }
}

public class McpConfiguration
{
    public bool Enabled { get; set; } = true;
    public List<McpServerConfig> Servers { get; set; } = new();
}

public class McpServerConfig
{
    public string Name { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    public Dictionary<string, string> Headers { get; set; } = new();
}