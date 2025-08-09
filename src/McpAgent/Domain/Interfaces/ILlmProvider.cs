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
    /// LLM Model 정보를 가져옵니다.
    /// </summary>
    /// <returns></returns>
    string GetLlmModel();
}