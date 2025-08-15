using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using McpAgent.Domain.Interfaces;

namespace McpAgent.Application.Services;

/// <summary>
/// HTML 시각화 생성 및 브라우저 표시 서비스
/// </summary>
public class HtmlVisualizationService : IHtmlVisualizationService
{
    private readonly ILogger<HtmlVisualizationService> _logger;
    private readonly ILlmProvider _llmProvider;
    
    public HtmlVisualizationService(
        ILogger<HtmlVisualizationService> logger,
        ILlmProvider llmProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _llmProvider = llmProvider ?? throw new ArgumentNullException(nameof(llmProvider));
    }

    public async Task<HtmlVisualizationResult> CreateAndOpenVisualizationAsync(
        string userRequest, 
        string? data = null, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("HTML 시각화 생성 시작: {Request}", userRequest);
            
            // 1. LLM을 통해 HTML 생성
            var htmlContent = await GenerateHtmlAsync(userRequest, data, cancellationToken);
            
            if (string.IsNullOrEmpty(htmlContent))
            {
                return HtmlVisualizationResult.Failure("HTML 생성에 실패했습니다.");
            }
            
            // 2. 임시 파일로 저장
            var filePath = await SaveHtmlToTempFileAsync(htmlContent);
            
            // 3. 브라우저에서 열기
            var openSuccess = await OpenInBrowserAsync(filePath);
            
            if (openSuccess)
            {
                _logger.LogInformation("HTML 시각화 완료: {FilePath}", filePath);
                return HtmlVisualizationResult.Success(filePath, htmlContent);
            }
            else
            {
                return HtmlVisualizationResult.Failure($"브라우저에서 파일을 열 수 없습니다: {filePath}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HTML 시각화 생성 중 오류 발생");
            return HtmlVisualizationResult.Failure($"오류 발생: {ex.Message}");
        }
    }

    private async Task<string> GenerateHtmlAsync(string userRequest, string? data, CancellationToken cancellationToken)
    {
        var prompt = BuildHtmlGenerationPrompt(userRequest, data);
        
        try
        {
            var response = await _llmProvider.GenerateResponseAsync(prompt, cancellationToken);
            
            // HTML 태그로 둘러싸인 부분만 추출
            var htmlContent = ExtractHtmlFromResponse(response);
            return htmlContent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM HTML 생성 중 오류");
            return string.Empty;
        }
    }

    private static string BuildHtmlGenerationPrompt(string userRequest, string? data)
    {
        var promptBuilder = new StringBuilder();
        
        promptBuilder.AppendLine("당신은 HTML 시각화 전문가입니다. 사용자의 요청에 따라 완전한 HTML 페이지를 생성해주세요.");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("## 요구사항:");
        promptBuilder.AppendLine("1. 완전한 HTML 문서를 만들어주세요 (<!DOCTYPE html>부터 </html>까지)");
        promptBuilder.AppendLine("2. 필요한 CSS와 JavaScript는 인라인으로 포함시켜주세요");
        promptBuilder.AppendLine("3. 외부 라이브러리가 필요하면 CDN 링크를 사용해주세요 (Chart.js, D3.js 등)");
        promptBuilder.AppendLine("4. 반응형 디자인을 적용해주세요");
        promptBuilder.AppendLine("5. 깔끔하고 현대적인 디자인을 사용해주세요");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine($"## 사용자 요청:");
        promptBuilder.AppendLine(userRequest);
        
        if (!string.IsNullOrEmpty(data))
        {
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("## 시각화할 데이터:");
            promptBuilder.AppendLine(data);
        }
        
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("## 출력 형식:");
        promptBuilder.AppendLine("HTML 코드만 출력해주세요. 설명이나 추가 텍스트는 필요하지 않습니다.");
        promptBuilder.AppendLine("```html로 감싸지 말고 HTML 코드를 직접 출력해주세요.");
        
        return promptBuilder.ToString();
    }

    private static string ExtractHtmlFromResponse(string response)
    {
        // ```html로 감싸진 경우 추출
        var htmlStart = response.IndexOf("```html", StringComparison.OrdinalIgnoreCase);
        if (htmlStart >= 0)
        {
            htmlStart += 7; // "```html".Length
            var htmlEnd = response.IndexOf("```", htmlStart);
            if (htmlEnd > htmlStart)
            {
                return response.Substring(htmlStart, htmlEnd - htmlStart).Trim();
            }
        }
        
        // <!DOCTYPE html로 시작하는 경우 그대로 사용
        var doctypeStart = response.IndexOf("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase);
        if (doctypeStart >= 0)
        {
            return response.Substring(doctypeStart).Trim();
        }
        
        // <html로 시작하는 경우 그대로 사용
        var htmlTagStart = response.IndexOf("<html", StringComparison.OrdinalIgnoreCase);
        if (htmlTagStart >= 0)
        {
            return response.Substring(htmlTagStart).Trim();
        }
        
        // 그 외의 경우 전체 응답 사용
        return response.Trim();
    }

    private async Task<string> SaveHtmlToTempFileAsync(string htmlContent)
    {
        var tempPath = Path.GetTempPath();
        var fileName = $"visualization_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..8]}.html";
        var filePath = Path.Combine(tempPath, fileName);
        
        await File.WriteAllTextAsync(filePath, htmlContent, Encoding.UTF8);
        
        _logger.LogInformation("HTML 파일 저장 완료: {FilePath}", filePath);
        return filePath;
    }

    private async Task<bool> OpenInBrowserAsync(string filePath)
    {
        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            };
            
            using var process = Process.Start(processStartInfo);
            
            // 프로세스 시작 대기 (짧은 시간)
            await Task.Delay(1000);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "브라우저에서 파일 열기 실패: {FilePath}", filePath);
            return false;
        }
    }
}