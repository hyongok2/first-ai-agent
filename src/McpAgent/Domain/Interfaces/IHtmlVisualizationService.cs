namespace McpAgent.Domain.Interfaces;

/// <summary>
/// HTML 시각화 생성 및 브라우저 표시 서비스 인터페이스
/// </summary>
public interface IHtmlVisualizationService
{
    /// <summary>
    /// 사용자 요청에 따라 HTML 시각화를 생성하고 브라우저에서 열기
    /// </summary>
    /// <param name="userRequest">사용자의 시각화 요청</param>
    /// <param name="data">시각화할 데이터 (JSON 형태)</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>생성된 HTML 파일 경로와 실행 결과</returns>
    Task<HtmlVisualizationResult> CreateAndOpenVisualizationAsync(
        string userRequest, 
        string? data = null, 
        CancellationToken cancellationToken = default);
}

/// <summary>
/// HTML 시각화 결과
/// </summary>
public class HtmlVisualizationResult
{
    public bool IsSuccess { get; init; }
    public string? FilePath { get; init; }
    public string? ErrorMessage { get; init; }
    public string? GeneratedHtml { get; init; }
    
    public static HtmlVisualizationResult Success(string filePath, string generatedHtml)
        => new() { IsSuccess = true, FilePath = filePath, GeneratedHtml = generatedHtml };
    
    public static HtmlVisualizationResult Failure(string errorMessage)
        => new() { IsSuccess = false, ErrorMessage = errorMessage };
}