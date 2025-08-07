namespace McpAgent.Domain.Entities;

public class ConversationMessage
{
    public string Id { get; }
    public MessageRole Role { get; }
    public string Content { get; }
    public DateTime Timestamp { get; }
    public Dictionary<string, object> Metadata { get; }

    public ConversationMessage(MessageRole role, string content, Dictionary<string, object>? metadata = null)
    {
        Id = Guid.NewGuid().ToString();
        Role = role;
        Content = content ?? string.Empty;
        Timestamp = DateTime.UtcNow;
        Metadata = metadata ?? new Dictionary<string, object>();
    }

    // For deserialization
    public ConversationMessage(string id, MessageRole role, string content, DateTime timestamp, Dictionary<string, object> metadata)
    {
        Id = id;
        Role = role;
        Content = content;
        Timestamp = timestamp;
        Metadata = metadata;
    }
}

public enum MessageRole
{
    System,
    User,
    Assistant,
    Tool
}