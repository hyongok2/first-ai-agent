namespace McpAgent.Domain.Entities;

public class ToolCall
{
    public string Name { get; }
    public Dictionary<string, object> Arguments { get; }
    public object? Result { get; private set; }
    public bool IsSuccess { get; private set; }
    public string Error { get; private set; }
    public DateTime ExecutedAt { get; private set; }
    public TimeSpan ExecutionDuration { get; private set; }

    public ToolCall(string name, Dictionary<string, object>? arguments = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Arguments = arguments ?? new Dictionary<string, object>();
        IsSuccess = false;
        Error = string.Empty;
        ExecutedAt = DateTime.UtcNow;
    }

    public void SetResult(object result, TimeSpan executionDuration)
    {
        Result = result;
        IsSuccess = true;
        Error = string.Empty;
        ExecutionDuration = executionDuration;
    }

    public void SetError(string error, TimeSpan executionDuration)
    {
        Result = null;
        IsSuccess = false;
        Error = error ?? string.Empty;
        ExecutionDuration = executionDuration;
    }
}