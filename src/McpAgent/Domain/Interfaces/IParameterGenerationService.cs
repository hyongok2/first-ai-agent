using McpAgent.Domain.Entities;

namespace McpAgent.Domain.Interfaces;

/// <summary>
/// MCP 도구 실행을 위한 파라미터 생성을 담당하는 서비스
/// </summary>
public interface IParameterGenerationService
{
    /// <summary>
    /// 선택된 MCP 도구에 대한 실행 파라미터를 생성합니다.
    /// </summary>
    /// <param name="toolName">실행할 도구 이름</param>
    /// <param name="toolDefinition">도구 정의 정보</param>
    /// <param name="refinedInput">정제된 사용자 입력</param>
    /// <param name="conversationHistory">대화 이력</param>
    /// <param name="systemContext">시스템 컨텍스트</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>생성된 파라미터</returns>
    Task<Dictionary<string, object>> GenerateParametersAsync(
        string toolName,
        ToolDefinition toolDefinition,
        RefinedInput refinedInput,
        IReadOnlyList<ConversationMessage> conversationHistory,
        string systemContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// MCP 도구 선택을 위한 추천을 제공합니다.
    /// </summary>
    /// <param name="availableTools">사용 가능한 도구 목록</param>
    /// <param name="refinedInput">정제된 사용자 입력</param>
    /// <param name="systemContext">시스템 컨텍스트</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>추천된 도구 이름</returns>
    Task<string> RecommendToolAsync(
        IReadOnlyList<ToolDefinition> availableTools,
        RefinedInput refinedInput,
        string systemContext,
        CancellationToken cancellationToken = default);
}