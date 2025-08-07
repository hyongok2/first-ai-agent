namespace McpAgent.Domain.Interfaces;

/// <summary>
/// 프롬프트 템플릿 관리 서비스
/// </summary>
public interface IPromptService
{
    /// <summary>
    /// 지정된 이름의 프롬프트 템플릿을 로드합니다.
    /// </summary>
    /// <param name="promptName">프롬프트 이름</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>프롬프트 내용</returns>
    Task<string> GetPromptAsync(string promptName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 모든 프롬프트 템플릿을 로드합니다.
    /// </summary>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>프롬프트 이름과 내용의 딕셔너리</returns>
    Task<Dictionary<string, string>> GetAllPromptsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 프롬프트 캐시를 지웁니다.
    /// </summary>
    void ClearCache();

    /// <summary>
    /// 지정된 프롬프트가 캐시되어 있는지 확인합니다.
    /// </summary>
    /// <param name="promptName">프롬프트 이름</param>
    /// <returns>캐시 여부</returns>
    bool IsPromptCached(string promptName);
}