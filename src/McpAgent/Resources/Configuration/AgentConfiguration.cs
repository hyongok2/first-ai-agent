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