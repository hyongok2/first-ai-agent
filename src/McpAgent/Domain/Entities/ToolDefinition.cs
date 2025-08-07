namespace McpAgent.Domain.Entities;

/// <summary>
/// MCP 도구 정의
/// </summary>
public class ToolDefinition
{
    /// <summary>
    /// 도구 이름
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// 도구 설명
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// 도구 파라미터 정의
    /// </summary>
    public Dictionary<string, ParameterDefinition> Parameters { get; set; } = new();

    /// <summary>
    /// JSON 스키마 (선택적)
    /// </summary>
    public string? Schema { get; set; }

    /// <summary>
    /// 도구 카테고리
    /// </summary>
    public string Category { get; set; } = "general";

    /// <summary>
    /// 도구가 사용 가능한지 여부
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 도구 버전
    /// </summary>
    public string Version { get; set; } = "1.0";
}