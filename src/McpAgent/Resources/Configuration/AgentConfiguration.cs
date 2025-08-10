namespace McpAgent.Configuration;

public class AgentConfiguration
{
    public LlmConfiguration Llm { get; set; } = new();
    public McpConfiguration Mcp { get; set; } = new();
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
    public string Command { get; set; } = string.Empty;
    public List<string> Args { get; set; } = new();
    public Dictionary<string, string> Env { get; set; } = new();
}