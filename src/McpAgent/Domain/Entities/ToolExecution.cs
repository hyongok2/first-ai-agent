namespace McpAgent.Domain.Entities;

/// <summary>
/// 도구 실행 결과를 나타내는 엔티티
/// </summary>
public class ToolExecution
{
    /// <summary>
    /// 실행된 도구 이름
    /// </summary>
    public string ToolName { get; set; } = "";

    /// <summary>
    /// 도구에 전달된 파라미터
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();

    /// <summary>
    /// 실행 결과
    /// </summary>
    public object? Result { get; set; }

    /// <summary>
    /// 실행 성공 여부
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// 오류 메시지 (실행 실패 시)
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 실행 시작 시간
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// 실행 완료 시간
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    /// 실행 소요 시간 (밀리초)
    /// </summary>
    public long DurationMs => (long)(EndTime - StartTime).TotalMilliseconds;

    /// <summary>
    /// 실행 ID
    /// </summary>
    public string ExecutionId { get; set; } = Guid.NewGuid().ToString();
}