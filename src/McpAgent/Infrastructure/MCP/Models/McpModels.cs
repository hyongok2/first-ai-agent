using System.Text.Json.Serialization;

namespace McpAgent.Infrastructure.MCP.Models;

// MCP Protocol Models

public class McpRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public object Id { get; set; } = 1;

    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("params")]
    public object? Params { get; set; }
}

public class McpResponse<T>
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("id")]
    public object Id { get; set; } = 1;

    [JsonPropertyName("result")]
    public T? Result { get; set; }

    [JsonPropertyName("error")]
    public McpError? Error { get; set; }
}

public class McpError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public object? Data { get; set; }
}

// Initialize Request/Response
public class InitializeParams
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = "2025-06-18";

    [JsonPropertyName("capabilities")]
    public InitializeCapabilities Capabilities { get; set; } = new();

    [JsonPropertyName("clientInfo")]
    public ClientInfo ClientInfo { get; set; } = new();
}

public class InitializeCapabilities
{
    [JsonPropertyName("tools")]
    public bool? Tools { get; set; } = true;

    [JsonPropertyName("resources")]
    public bool? Resources { get; set; }= false;

    [JsonPropertyName("prompts")]
    public bool? Prompts { get; set; }= false;

    [JsonPropertyName("sampling")]
    public bool? Sampling { get; set; }= false;

    [JsonPropertyName("logging")]
    public bool? Logging { get; set; }= true;
}

public class ClientInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "McpAgent";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";
}

public class InitializeResult
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = "2025-06-18";

    [JsonPropertyName("capabilities")]
    public ServerCapabilities Capabilities { get; set; } = new();

    [JsonPropertyName("serverInfo")]
    public ServerInfo ServerInfo { get; set; } = new();
}

public class ServerCapabilities
{
    [JsonPropertyName("tools")]
    public ToolCapabilities? Tools { get; set; }
}

public class ToolCapabilities
{
    [JsonPropertyName("listChanged")]
    public bool? ListChanged { get; set; }
}

public class ServerInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
}

// Tools List Request/Response
public class ListToolsResult
{
    [JsonPropertyName("tools")]
    public List<McpTool> Tools { get; set; } = new();
}

public class McpTool
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("inputSchema")]
    public McpToolInputSchema InputSchema { get; set; } = new();
}

public class McpToolInputSchema
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "object";

    [JsonPropertyName("properties")]
    public Dictionary<string, McpProperty> Properties { get; set; } = new();

    [JsonPropertyName("required")]
    public List<string>? Required { get; set; }
}

public class McpProperty
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

// Call Tool Request/Response
public class CallToolParams
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public Dictionary<string, object> Arguments { get; set; } = new();
}

public class CallToolResult
{
    [JsonPropertyName("content")]
    public List<ToolContent> Content { get; set; } = new();

    [JsonPropertyName("isError")]
    public bool? IsError { get; set; }
}

public class ToolContent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

