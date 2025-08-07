using McpAgent.Domain.Entities;

namespace McpAgent.Domain.Interfaces;

/// <summary>
/// LLM 공급자 인터페이스 - 다단계 파이프라인용
/// </summary>
public interface ILlmProvider
{
    /// <summary>
    /// 프롬프트에 대한 응답을 생성합니다.
    /// </summary>
    /// <param name="prompt">LLM에 전달할 프롬프트</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>LLM 응답</returns>
    Task<string> GenerateResponseAsync(string prompt, CancellationToken cancellationToken = default);

    /// <summary>
    /// 도구 사용을 고려한 응답을 생성합니다.
    /// </summary>
    /// <param name="prompt">프롬프트</param>
    /// <param name="availableTools">사용 가능한 도구들</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>LLM 응답</returns>
    Task<string> GenerateResponseWithToolsAsync(
        string prompt, 
        IReadOnlyList<ToolDefinition> availableTools, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 스트리밍 응답을 생성합니다.
    /// </summary>
    /// <param name="prompt">프롬프트</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>스트리밍 응답</returns>
    IAsyncEnumerable<string> GenerateStreamingResponseAsync(string prompt, CancellationToken cancellationToken = default);
}