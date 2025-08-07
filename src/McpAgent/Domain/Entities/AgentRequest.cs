namespace McpAgent.Domain.Entities;

public class AgentRequest
{
    public string Message { get; }
    public string ConversationId { get; }
    public Dictionary<string, object> Context { get; }
    public List<string> EnabledTools { get; }
    public DateTime Timestamp { get; }

    public AgentRequest(
        string message, 
        string conversationId, 
        Dictionary<string, object>? context = null,
        List<string>? enabledTools = null)
    {
        Message = message ?? throw new ArgumentNullException(nameof(message));
        ConversationId = conversationId ?? throw new ArgumentNullException(nameof(conversationId));
        Context = context ?? new Dictionary<string, object>();
        EnabledTools = enabledTools ?? new List<string>();
        Timestamp = DateTime.UtcNow;
    }
}