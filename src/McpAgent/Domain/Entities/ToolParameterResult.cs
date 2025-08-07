namespace McpAgent.Domain.Entities;

/// <summary>
/// MCP 도구 파라미터 생성 결과
/// </summary>
public class ToolParameterResult
{
    /// <summary>
    /// 대상 도구 이름
    /// </summary>
    public string ToolName { get; set; } = "";

    /// <summary>
    /// 생성된 파라미터들
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();

    /// <summary>
    /// 파라미터 검증 노트
    /// </summary>
    public string ValidationNotes { get; set; } = "";

    /// <summary>
    /// 누락된 정보에 대한 설명
    /// </summary>
    public string MissingInfo { get; set; } = "";

    /// <summary>
    /// 파라미터의 유효성
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// 파라미터가 생성된 시간
    /// </summary>
    public DateTime GeneratedAt { get; set; }

    /// <summary>
    /// 파라미터 생성에 대한 신뢰도 (0.0 ~ 1.0)
    /// </summary>
    public double ConfidenceScore { get; set; }
}