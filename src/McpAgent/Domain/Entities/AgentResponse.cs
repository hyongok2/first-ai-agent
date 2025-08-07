namespace McpAgent.Domain.Entities;

public class AgentResponse
{
    public string Message { get; }
    public string ConversationId { get; }
    public bool IsSuccess { get; }
    public string Error { get; }
    public List<ToolCall> ToolCalls { get; }
    public Dictionary<string, object> Metadata { get; }
    public DateTime Timestamp { get; }
    public int ToolChainLength { get; }
    public bool ChainTerminated { get; }

    private AgentResponse(
        string message,
        string conversationId,
        bool isSuccess,
        string error,
        List<ToolCall>? toolCalls = null,
        Dictionary<string, object>? metadata = null,
        int toolChainLength = 0,
        bool chainTerminated = false)
    {
        Message = message ?? string.Empty;
        ConversationId = conversationId ?? string.Empty;
        IsSuccess = isSuccess;
        Error = error ?? string.Empty;
        ToolCalls = toolCalls ?? new List<ToolCall>();
        Metadata = metadata ?? new Dictionary<string, object>();
        Timestamp = DateTime.UtcNow;
        ToolChainLength = toolChainLength;
        ChainTerminated = chainTerminated;
    }

    public static AgentResponse Success(
        string message, 
        string conversationId,
        List<ToolCall>? toolCalls = null,
        Dictionary<string, object>? metadata = null,
        int toolChainLength = 0,
        bool chainTerminated = false)
    {
        return new AgentResponse(message, conversationId, true, string.Empty, 
            toolCalls, metadata, toolChainLength, chainTerminated);
    }

    public static AgentResponse Failure(string error, string conversationId)
    {
        return new AgentResponse(string.Empty, conversationId, false, error);
    }
}