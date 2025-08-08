using System.Text;
using Microsoft.Extensions.Logging;

namespace McpAgent.Shared.Utils;

public class TokenCounter
{
    private const double TokensPerWord = 1.3; // Approximate for most models
    private const double TokensPerCharacter = 0.25; // Approximate for Asian languages
    private readonly ILogger<TokenCounter> _logger;

    public TokenCounter(ILogger<TokenCounter> logger)
    {
        _logger = logger;
    }

    public int CountTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

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

        // Mixed or non-English - use character-based estimation
        return (int)Math.Ceiling(charCount * TokensPerCharacter);
    }

    public bool WouldExceedLimit(List<string> texts, int maxTokens)
    {
        var totalTokens = texts.Sum(CountTokens);
        return totalTokens > maxTokens;
    }

    public List<string> TrimToTokenLimit(List<string> texts, int maxTokens)
    {
        var result = new List<string>();
        var currentTokens = 0;

        for (int i = texts.Count - 1; i >= 0; i--)
        {
            var text = texts[i];
            var textTokens = CountTokens(text);

            if (currentTokens + textTokens > maxTokens) break;

            result.Insert(0, text);
            currentTokens += textTokens;
        }

        return result;
    }

    public int EstimateTokens(string text) => CountTokens(text);

    private static int CountEnglishCharacters(string text)
    {
        return text.Count(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'));
    }
}