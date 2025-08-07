namespace McpAgent.Domain.Entities;

/// <summary>
/// 선택된 시스템 능력 정보
/// </summary>
public class SelectedCapability
{
    /// <summary>
    /// 선택된 능력 유형
    /// </summary>
    public SystemCapabilityType CapabilityType { get; set; }

    /// <summary>
    /// 선택 이유에 대한 간단한 설명
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// 선택 근거에 대한 상세 설명
    /// </summary>
    public string Reasoning { get; set; } = "";

    /// <summary>
    /// 추가 파라미터 정보
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();

    /// <summary>
    /// 선택된 시간
    /// </summary>
    public DateTime SelectedAt { get; set; }

    /// <summary>
    /// 신뢰도 점수 (0.0 ~ 1.0)
    /// </summary>
    public double ConfidenceScore { get; set; }
}