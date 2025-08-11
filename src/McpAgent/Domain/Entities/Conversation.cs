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

    /// <summary>
    /// 제한된 개수의 최근 메시지를 가져옵니다
    /// </summary>
    /// <param name="maxCount">가져올 최대 메시지 개수</param>
    /// <returns>최근 메시지들</returns>
    public IReadOnlyList<ConversationMessage> GetRecentMessages(int maxCount)
    {
        if (maxCount <= 0) return Array.Empty<ConversationMessage>();
        
        var startIndex = Math.Max(0, Messages.Count - maxCount);
        return Messages.Skip(startIndex).ToList().AsReadOnly();
    }

    public ConversationMessage? GetLastMessage() => Messages.LastOrDefault();
}

public enum ConversationStatus
{
    Active,
    Paused,
    Completed,
    Error
}