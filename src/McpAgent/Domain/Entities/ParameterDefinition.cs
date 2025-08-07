namespace McpAgent.Domain.Entities;

/// <summary>
/// MCP 도구 파라미터 정의
/// </summary>
public class ParameterDefinition
{
    /// <summary>
    /// 파라미터 타입 (string, number, boolean, array, object)
    /// </summary>
    public string Type { get; set; } = "string";

    /// <summary>
    /// 파라미터 설명
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// 파라미터가 필수인지 여부
    /// </summary>
    public bool Required { get; set; } = false;

    /// <summary>
    /// 기본값 (선택적)
    /// </summary>
    public object? DefaultValue { get; set; }

    /// <summary>
    /// 가능한 값들의 열거형 (선택적)
    /// </summary>
    public IReadOnlyList<object>? Enum { get; set; }

    /// <summary>
    /// 최소값 (숫자 타입인 경우)
    /// </summary>
    public double? Minimum { get; set; }

    /// <summary>
    /// 최대값 (숫자 타입인 경우)
    /// </summary>
    public double? Maximum { get; set; }

    /// <summary>
    /// 최소 길이 (문자열 타입인 경우)
    /// </summary>
    public int? MinLength { get; set; }

    /// <summary>
    /// 최대 길이 (문자열 타입인 경우)
    /// </summary>
    public int? MaxLength { get; set; }

    /// <summary>
    /// 정규식 패턴 (문자열 타입인 경우)
    /// </summary>
    public string? Pattern { get; set; }
}