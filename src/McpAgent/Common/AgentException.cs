namespace McpAgent.Common;

public class AgentException : Exception
{
    public string ErrorCode { get; }
    
    public AgentException(string errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }
    
    public AgentException(string errorCode, string message, Exception innerException) 
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}

public class LlmProviderException : AgentException
{
    public LlmProviderException(string message) : base("LLM_PROVIDER_ERROR", message) { }
    public LlmProviderException(string message, Exception innerException) : base("LLM_PROVIDER_ERROR", message, innerException) { }
}

public class McpConnectionException : AgentException
{
    public string ServerName { get; }
    
    public McpConnectionException(string serverName, string message) : base("MCP_CONNECTION_ERROR", message)
    {
        ServerName = serverName;
    }
    
    public McpConnectionException(string serverName, string message, Exception innerException) 
        : base("MCP_CONNECTION_ERROR", message, innerException)
    {
        ServerName = serverName;
    }
}

public class ConversationException : AgentException
{
    public string ConversationId { get; }
    
    public ConversationException(string conversationId, string message) : base("CONVERSATION_ERROR", message)
    {
        ConversationId = conversationId;
    }
}