namespace McpAgent.Models;

public class AgentRequest
{
    public string Message { get; set; } = string.Empty;
    public string ConversationId { get; set; } = Guid.NewGuid().ToString();
    public Dictionary<string, object> Context { get; set; } = new();
    public List<string> EnabledTools { get; set; } = new();
}

public class AgentResponse
{
    public string Message { get; set; } = string.Empty;
    public string ConversationId { get; set; } = string.Empty;
    public bool IsSuccess { get; set; } = true;
    public string Error { get; set; } = string.Empty;
    public List<ToolCall> ToolCalls { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public int ToolChainLength { get; set; } = 0;
    public bool ChainTerminated { get; set; } = false;
}

public class ToolCall
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, object> Arguments { get; set; } = new();
    public object? Result { get; set; }
    public bool IsSuccess { get; set; } = true;
    public string Error { get; set; } = string.Empty;
}

public class ConversationMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object> Metadata { get; set; } = new();
}