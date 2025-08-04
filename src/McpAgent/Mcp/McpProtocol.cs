using System.Text.Json;

namespace McpAgent.Mcp;

public static class McpProtocol
{
    public const string ProtocolVersion = "2024-11-05";
    
    public static class Methods
    {
        public const string Initialize = "initialize";
        public const string ToolsList = "tools/list";
        public const string ToolsCall = "tools/call";
        public const string ResourcesList = "resources/list";
        public const string ResourcesRead = "resources/read";
    }
}

public class McpRequest
{
    public string JsonRpc { get; set; } = "2.0";
    public int Id { get; set; }
    public string Method { get; set; } = string.Empty;
    public object? Params { get; set; }
}

public class McpResponse
{
    public string JsonRpc { get; set; } = "2.0";
    public int Id { get; set; }
    public object? Result { get; set; }
    public McpError? Error { get; set; }
}

public class McpError
{
    public int Code { get; set; }
    public string Message { get; set; } = string.Empty;
    public object? Data { get; set; }
}

public class InitializeParams
{
    public string ProtocolVersion { get; set; } = McpProtocol.ProtocolVersion;
    public Dictionary<string, object> Capabilities { get; set; } = new();
    public ClientInfo ClientInfo { get; set; } = new();
}

public class ClientInfo
{
    public string Name { get; set; } = "McpAgent";
    public string Version { get; set; } = "1.0.0";
}

public class ToolCallParams
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, object> Arguments { get; set; } = new();
}