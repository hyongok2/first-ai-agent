namespace McpAgent.Domain.Entities;

public class Conversation
{
    public string Id { get; }
    public List<ConversationMessage> Messages { get; }
    public DateTime CreatedAt { get; }
    public DateTime LastActivity { get; private set; }
    public ConversationStatus Status { get; private set; }

    public Conversation(string id)
    {
        Id = id;
        Messages = new List<ConversationMessage>();
        CreatedAt = DateTime.UtcNow;
        LastActivity = DateTime.UtcNow;
        Status = ConversationStatus.Active;
    }

    public void AddMessage(ConversationMessage message)
    {
        Messages.Add(message);
        LastActivity = DateTime.UtcNow;
    }

    public void SetStatus(ConversationStatus status)
    {
        Status = status;
        LastActivity = DateTime.UtcNow;
    }

    public IReadOnlyList<ConversationMessage> GetMessages() => Messages.AsReadOnly();

    public ConversationMessage? GetLastMessage() => Messages.LastOrDefault();
}

public enum ConversationStatus
{
    Active,
    Paused,
    Completed,
    Error
}