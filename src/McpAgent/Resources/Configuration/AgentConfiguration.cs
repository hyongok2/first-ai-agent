namespace McpAgent.Configuration;

public class AgentConfiguration
{
    public LlmConfiguration Llm { get; set; } = new();
    public McpConfiguration Mcp { get; set; } = new();
    public AgentSettings Agent { get; set; } = new();
}

public class LlmConfiguration
{
    public string Provider { get; set; } = "ollama";
    public string Endpoint { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "llama3";
    public int MaxTokens { get; set; } = 8192;
    public int MaxToolContextTokens { get; set; } = 2048;
    public int MaxHistoryTokens { get; set; } = 2048;
    public double Temperature { get; set; } = 0.7;
    
    /// <summary>
    /// 모델별 컨텍스트 윈도우 크기 설정
    /// </summary>
    public Dictionary<string, int> ContextWindows { get; set; } = new()
    {
        { "llama3.1:8b", 32768 },
        { "llama3.1:7b", 32768 },
        { "llama3.1", 32768 },
        { "llama3:8b", 8192 },
        { "llama3:7b", 8192 },
        { "llama3", 8192 },
        { "mistral:7b", 8192 },
        { "mistral", 8192 },
        { "codellama:7b", 16384 },
        { "codellama", 16384 },
        { "qwen2:7b", 32768 },
        { "qwen2", 32768 },
        { "phi3:mini", 4096 },
        { "gemma:7b", 8192 },
        { "neural-chat:7b", 4096 },
        { "gpt-oss:20b", 32768 },
        { "qwen3:32b", 32768 },
        { "qwen3:30b", 32768 },
        { "qwen3:14b", 32768 }
    };
    
    /// <summary>
    /// 기본 컨텍스트 윈도우 크기 (알 수 없는 모델일 때 사용)
    /// </summary>
    public int DefaultContextWindowSize { get; set; } = 8192;
    
    /// <summary>
    /// 응답 생성용 예약 토큰 수
    /// </summary>
    public int ReservedTokensForResponse { get; set; } = 2048;
    
    /// <summary>
    /// 안전 여백 토큰 수
    /// </summary>
    public int SafetyMarginTokens { get; set; } = 512;
}

public class McpConfiguration
{
    public bool Enabled { get; set; } = true;
    public List<McpServerConfig> Servers { get; set; } = new();
    public int HealthCheckIntervalSeconds { get; set; } = 30;
    public int RequestTimeoutSeconds { get; set; } = 30;
}

public class McpServerConfig
{
    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public List<string> Args { get; set; } = new();
    public Dictionary<string, string> Env { get; set; } = new();
}

public class AgentSettings
{
    public string Name { get; set; } = "AI Assistant";
    public string SystemPrompt { get; set; } = "You are a helpful AI assistant.";
    public int MaxHistoryLength { get; set; } = 50;
    public bool EnableLogging { get; set; } = true;
    public int MaxToolChainIterations { get; set; } = 5;
    public bool EnableToolChaining { get; set; } = true;
    public string PromptStyle { get; set; } = "direct"; // "direct" or "react"
}