using System.Text;

namespace McpAgent.Utils;

public static class TokenCounter
{
    private const double TokensPerWord = 1.3; // Approximate for most models
    private const double TokensPerCharacter = 0.25; // Approximate for Asian languages
    
    public static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;
            
        // Simple estimation - can be improved with actual tokenizer
        var wordCount = text.Split(new[] { ' ', '\n', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
        var charCount = text.Length;
        
        // Use different estimation for different character ratios
        var englishRatio = CountEnglishCharacters(text) / (double)charCount;
        
        if (englishRatio > 0.7)
        {
            // Mostly English - use word-based estimation
            return (int)Math.Ceiling(wordCount * TokensPerWord);
        }
        else
        {
            // Mixed or non-English - use character-based estimation
            return (int)Math.Ceiling(charCount * TokensPerCharacter);
        }
    }
    
    public static bool WouldExceedLimit(List<string> texts, int maxTokens)
    {
        var totalTokens = texts.Sum(EstimateTokens);
        return totalTokens > maxTokens;
    }
    
    public static List<string> TrimToTokenLimit(List<string> texts, int maxTokens)
    {
        var result = new List<string>();
        var currentTokens = 0;
        
        for (int i = texts.Count - 1; i >= 0; i--)
        {
            var text = texts[i];
            var textTokens = EstimateTokens(text);
            if (currentTokens + textTokens <= maxTokens)
            {
                result.Insert(0, text);
                currentTokens += textTokens;
            }
            else
            {
                break;
            }
        }
        
        return result;
    }
    
    private static int CountEnglishCharacters(string text)
    {
        return text.Count(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'));
    }
}