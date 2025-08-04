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