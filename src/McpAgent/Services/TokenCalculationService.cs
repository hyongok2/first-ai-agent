using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using McpAgent.Configuration;

namespace McpAgent.Services;

/// <summary>
/// 토큰 계산 및 관리를 위한 서비스
/// </summary>
public interface ITokenCalculationService
{
    /// <summary>
    /// 텍스트의 토큰 수 추정
    /// </summary>
    int EstimateTokenCount(string text);
    
    /// <summary>
    /// 여러 텍스트의 총 토큰 수 추정
    /// </summary>
    int EstimateTokenCount(IEnumerable<string> texts);
    
    /// <summary>
    /// 토큰 제한에 맞게 텍스트 목록 필터링
    /// </summary>
    List<string> FilterByTokenLimit(List<string> texts, int maxTokens);
    
    /// <summary>
    /// 토큰 제한에 맞게 텍스트 압축
    /// </summary>
    string CompressToTokenLimit(string text, int maxTokens);
    
    /// <summary>
    /// 현재 LLM의 컨텍스트 윈도우 크기 반환
    /// </summary>
    int GetContextWindowSize();
    
    /// <summary>
    /// 응답 생성용 예약 토큰 수 반환
    /// </summary>
    int GetReservedTokensForResponse();
    
    /// <summary>
    /// 히스토리 관리용 사용 가능한 토큰 수 반환
    /// </summary>
    int GetAvailableTokensForHistory();
    
    /// <summary>
    /// 압축 레벨 결정
    /// </summary>
    CompressionLevel DetermineCompressionLevel(int currentTokens, int availableTokens);
}

public class TokenCalculationService : ITokenCalculationService
{
    private readonly ILogger<TokenCalculationService> _logger;
    private readonly LlmConfiguration _llmConfig;
    
    public TokenCalculationService(ILogger<TokenCalculationService> logger, IOptions<AgentConfiguration> agentConfig)
    {
        _logger = logger;
        _llmConfig = agentConfig.Value.Llm;
        
        _logger.LogInformation("TokenCalculationService initialized with model: {Model}, context window: {ContextWindow}", 
            _llmConfig.Model, GetContextWindowSize());
    }
    
    /// <summary>
    /// GPT-3/4 기반 토큰 추정 (1 token ≈ 4 characters for English, 2-3 for Korean)
    /// </summary>
    public int EstimateTokenCount(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;
            
        try
        {
            // 기본적인 토큰 추정 방식
            // 더 정확한 토큰 계산을 원하면 tiktoken 라이브러리 사용 고려
            
            // 한글과 영문 분리 계산
            var koreanCharCount = Regex.Matches(text, @"[\u3131-\u3163\uac00-\ud7a3]").Count;
            var otherCharCount = text.Length - koreanCharCount;
            
            // 한글: 1.5-2.5 토큰/문자, 영문: 0.25 토큰/문자 (4문자/토큰)
            var estimatedTokens = (koreanCharCount * 2) + (otherCharCount / 4);
            
            // JSON, 특수문자 등을 고려한 보정
            if (text.Contains('{') || text.Contains('['))
                estimatedTokens = (int)(estimatedTokens * 1.2); // JSON은 20% 더 많은 토큰 사용
                
            if (text.Contains("```"))
                estimatedTokens = (int)(estimatedTokens * 1.1); // 코드블록 보정
            
            // 최소값 보장
            return Math.Max(1, estimatedTokens);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error estimating token count for text length {Length}", text.Length);
            
            // 폴백: 단순 길이 기반 추정
            return Math.Max(1, text.Length / 3);
        }
    }
    
    public int EstimateTokenCount(IEnumerable<string> texts)
    {
        return texts.Sum(EstimateTokenCount);
    }
    
    /// <summary>
    /// 토큰 제한에 맞게 텍스트 목록을 우선순위 기반으로 필터링
    /// </summary>
    public List<string> FilterByTokenLimit(List<string> texts, int maxTokens)
    {
        var result = new List<string>();
        var currentTokens = 0;
        
        // 최신 것부터 우선 선택 (역순으로 처리)
        for (int i = texts.Count - 1; i >= 0; i--)
        {
            var textTokens = EstimateTokenCount(texts[i]);
            
            if (currentTokens + textTokens <= maxTokens)
            {
                result.Insert(0, texts[i]); // 원래 순서 유지를 위해 앞에 삽입
                currentTokens += textTokens;
            }
            else
            {
                // 토큰 제한 초과시 중단
                _logger.LogDebug("Token limit reached. Selected {Count}/{Total} texts using {Tokens}/{MaxTokens} tokens", 
                    result.Count, texts.Count, currentTokens, maxTokens);
                break;
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// 토큰 제한에 맞게 단일 텍스트를 압축
    /// </summary>
    public string CompressToTokenLimit(string text, int maxTokens)
    {
        if (string.IsNullOrEmpty(text))
            return text;
            
        var currentTokens = EstimateTokenCount(text);
        
        if (currentTokens <= maxTokens)
            return text;
            
        // 압축 비율 계산 (안전 여백 10% 추가)
        var compressionRatio = (double)(maxTokens * 0.9) / currentTokens;
        var targetLength = (int)(text.Length * compressionRatio);
        
        if (targetLength < 50) // 너무 짧으면 의미 없음
        {
            return text.Substring(0, Math.Min(50, text.Length)) + "...";
        }
        
        // 문장 단위로 압축 (더 자연스러운 압축)
        var sentences = text.Split(new[] { '.', '!', '?', '。', '!', '?' }, 
            StringSplitOptions.RemoveEmptyEntries);
            
        if (sentences.Length <= 1)
        {
            // 문장 분리가 안되면 단순 자르기
            return text.Substring(0, Math.Min(targetLength, text.Length)) + "...";
        }
        
        // 문장 단위로 선택하여 토큰 제한 맞추기
        var compressedSentences = new List<string>();
        var compressedTokens = 0;
        
        foreach (var sentence in sentences)
        {
            var sentenceTokens = EstimateTokenCount(sentence);
            if (compressedTokens + sentenceTokens <= maxTokens * 0.9)
            {
                compressedSentences.Add(sentence.Trim());
                compressedTokens += sentenceTokens;
            }
            else
            {
                break;
            }
        }
        
        var result = string.Join(". ", compressedSentences);
        
        _logger.LogDebug("Compressed text from {OriginalTokens} to {CompressedTokens} tokens", 
            currentTokens, EstimateTokenCount(result));
            
        return result;
    }
    
    public int GetContextWindowSize()
    {
        // 설정에서 현재 모델의 컨텍스트 윈도우 크기 조회
        if (_llmConfig.ContextWindows.TryGetValue(_llmConfig.Model, out var windowSize))
        {
            return windowSize;
        }
        
        // 기본값 사용
        _logger.LogWarning("Unknown model {Model}, using default context window size {DefaultSize}", 
            _llmConfig.Model, _llmConfig.DefaultContextWindowSize);
        return _llmConfig.DefaultContextWindowSize;
    }
    
    public int GetReservedTokensForResponse()
    {
        return _llmConfig.ReservedTokensForResponse;
    }
    
    /// <summary>
    /// 사용 가능한 토큰 수 계산
    /// </summary>
    public int GetAvailableTokensForHistory()
    {
        var contextWindow = GetContextWindowSize();
        var reserved = GetReservedTokensForResponse();
        var safetyMargin = _llmConfig.SafetyMarginTokens;
        
        return Math.Max(1024, contextWindow - reserved - safetyMargin); // 최소 1K는 보장
    }
    
    /// <summary>
    /// 토큰 사용량 기반 압축 레벨 결정
    /// </summary>
    public CompressionLevel DetermineCompressionLevel(int currentTokens, int availableTokens)
    {
        var usageRatio = (double)currentTokens / availableTokens;
        
        return usageRatio switch
        {
            > 0.9 => CompressionLevel.Maximum,    // 90% 이상: 최대 압축
            > 0.7 => CompressionLevel.High,       // 70-90%: 높은 압축
            > 0.5 => CompressionLevel.Medium,     // 50-70%: 중간 압축
            > 0.3 => CompressionLevel.Low,        // 30-50%: 낮은 압축
            _ => CompressionLevel.None            // 30% 미만: 압축 없음
        };
    }
}

/// <summary>
/// 압축 레벨 정의
/// </summary>
public enum CompressionLevel
{
    None,      // 압축 없음
    Low,       // 최근 7개 대화 유지
    Medium,    // 최근 5개 대화 유지  
    High,      // 최근 3개 대화 유지
    Maximum    // 메타 요약만 사용
}