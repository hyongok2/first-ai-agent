using System.Text;
using System.Text.RegularExpressions;
using McpAgent.Models;
using McpAgent.Providers;
using McpAgent.Utils;
using Microsoft.Extensions.Logging;

namespace McpAgent.Services;

public class ContextManager : IContextManager
{
    private readonly ILogger<ContextManager> _logger;
    private readonly TokenCounter _tokenCounter;
    private readonly ILlmProvider _llmProvider;

    public ContextManager(
        ILogger<ContextManager> logger,
        TokenCounter tokenCounter,
        ILlmProvider llmProvider)
    {
        _logger = logger;
        _tokenCounter = tokenCounter;
        _llmProvider = llmProvider;
    }

    public async Task<OptimizedContext> OptimizeContextAsync(
        List<ConversationMessage> history,
        List<ToolDefinition> availableTools,
        string currentMessage,
        int maxTokens,
        CancellationToken cancellationToken = default)
    {
        var context = new OptimizedContext { AllTools = availableTools };

        // 1. Reserve tokens for current message and tools
        var currentMessageTokens = _tokenCounter.CountTokens(currentMessage);
        var toolsTokens = EstimateToolsTokens(availableTools);
        var reservedTokens = currentMessageTokens + toolsTokens + 500; // 500 for system prompt buffer

        var availableTokens = Math.Max(0, maxTokens - reservedTokens);
        
        _logger.LogDebug("Token allocation - Total: {Max}, Reserved: {Reserved}, Available: {Available}", 
            maxTokens, reservedTokens, availableTokens);

        if (availableTokens < 100) // Too few tokens for meaningful context
        {
            context.OptimizationStrategy = "minimal_context";
            context.RelevantTools = GetEssentialTools(availableTools, currentMessage);
            context.TokensUsed = reservedTokens;
            return context;
        }

        // 2. Analyze current message for tool relevance
        context.RelevantTools = GetRelevantTools(availableTools, currentMessage, history);
        
        // 3. Optimize history based on available tokens
        await OptimizeHistoryAsync(context, history, availableTokens, cancellationToken);

        context.TokensUsed = reservedTokens + 
            context.RecentMessages.Sum(m => _tokenCounter.CountTokens(m.Content)) +
            (context.HasSummary ? _tokenCounter.CountTokens(context.HistorySummary ?? "") : 0);

        return context;
    }

    private async Task OptimizeHistoryAsync(
        OptimizedContext context,
        List<ConversationMessage> history,
        int availableTokens,
        CancellationToken cancellationToken)
    {
        if (history.Count == 0)
        {
            context.OptimizationStrategy = "no_history";
            return;
        }

        // Strategy 1: Try to fit recent messages as-is
        var recentMessages = GetRecentImportantMessages(history, availableTokens);
        var recentTokens = recentMessages.Sum(m => _tokenCounter.CountTokens(m.Content));

        if (recentTokens <= availableTokens * 0.8) // Use 80% threshold for buffer
        {
            context.RecentMessages = recentMessages;
            context.OptimizationStrategy = "recent_messages";
            return;
        }

        // Strategy 2: Summarize older messages + keep recent ones
        var summaryTokenBudget = Math.Min(300, availableTokens / 3); // Max 300 tokens for summary
        var recentTokenBudget = availableTokens - summaryTokenBudget;

        var (messagesToSummarize, messagesToKeep) = SplitHistoryForSummary(history, recentTokenBudget);

        if (messagesToSummarize.Count > 0)
        {
            try
            {
                context.HistorySummary = await SummarizeHistoryAsync(messagesToSummarize, summaryTokenBudget, cancellationToken);
                context.HasSummary = true;
                context.RecentMessages = messagesToKeep;
                context.OptimizationStrategy = "summary_plus_recent";
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate history summary, falling back to recent messages only");
            }
        }

        // Strategy 3: Fallback - compress recent messages
        context.RecentMessages = CompressRecentMessages(history, availableTokens);
        context.OptimizationStrategy = "compressed_recent";
    }

    private List<ConversationMessage> GetRecentImportantMessages(List<ConversationMessage> history, int tokenBudget)
    {
        var result = new List<ConversationMessage>();
        var currentTokens = 0;

        // Prioritize: user questions, tool results, assistant responses
        var prioritizedMessages = history
            .Select((msg, index) => new { Message = msg, Index = index, Priority = GetMessagePriority(msg) })
            .OrderByDescending(x => x.Index) // Most recent first
            .ThenByDescending(x => x.Priority)
            .ToList();

        foreach (var item in prioritizedMessages)
        {
            var messageTokens = _tokenCounter.CountTokens(item.Message.Content);
            if (currentTokens + messageTokens <= tokenBudget)
            {
                result.Add(item.Message);
                currentTokens += messageTokens;
            }
            else if (result.Count == 0 && messageTokens > tokenBudget)
            {
                // If first message is too large, truncate it
                var truncated = TruncateMessage(item.Message, tokenBudget);
                result.Add(truncated);
                break;
            }
        }

        // Restore chronological order
        return result.OrderBy(m => history.IndexOf(m)).ToList();
    }

    private int GetMessagePriority(ConversationMessage message)
    {
        return message.Role switch
        {
            "user" => 3,      // Highest priority - user questions
            "tool" => 2,      // High priority - tool results
            "assistant" => 1, // Medium priority - responses
            _ => 0
        };
    }

    private (List<ConversationMessage> toSummarize, List<ConversationMessage> toKeep) SplitHistoryForSummary(
        List<ConversationMessage> history, int recentTokenBudget)
    {
        var toKeep = new List<ConversationMessage>();
        var currentTokens = 0;

        // Keep recent messages within budget
        for (int i = history.Count - 1; i >= 0; i--)
        {
            var message = history[i];
            var messageTokens = _tokenCounter.CountTokens(message.Content);
            
            if (currentTokens + messageTokens <= recentTokenBudget)
            {
                toKeep.Insert(0, message);
                currentTokens += messageTokens;
            }
            else
            {
                break;
            }
        }

        var splitIndex = history.Count - toKeep.Count;
        var toSummarize = history.Take(splitIndex).ToList();

        return (toSummarize, toKeep);
    }

    public async Task<string> SummarizeHistoryAsync(
        List<ConversationMessage> messages,
        int maxSummaryTokens,
        CancellationToken cancellationToken = default)
    {
        if (messages.Count == 0) return string.Empty;

        try
        {
            var conversationText = string.Join("\n", messages.Select(m => $"{m.Role}: {m.Content}"));
            
            var summaryPrompt = $@"Summarize this conversation history in {maxSummaryTokens/2} words or less. Focus on:
1. Key topics discussed  
2. Important information or decisions
3. Tool usage patterns
4. User preferences or context

Conversation:
{conversationText}

Summary:";

            var summary = await _llmProvider.GenerateResponseAsync(
                summaryPrompt,
                new List<ConversationMessage>(),
                new List<ToolDefinition>(),
                cancellationToken);

            // Ensure summary doesn't exceed token limit
            if (_tokenCounter.CountTokens(summary) > maxSummaryTokens)
            {
                summary = TruncateText(summary, maxSummaryTokens);
            }

            _logger.LogDebug("Generated history summary: {Length} chars, ~{Tokens} tokens", 
                summary.Length, _tokenCounter.CountTokens(summary));

            return $"[Previous conversation summary: {summary}]";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate conversation summary");
            
            // Fallback: Simple bullet point summary
            var keyPoints = messages
                .Where(m => m.Role == "user")
                .Take(3)
                .Select(m => TruncateText(m.Content, 50))
                .ToList();

            return keyPoints.Count > 0 
                ? $"[Previous topics: {string.Join(", ", keyPoints)}]"
                : "[Previous conversation context]";
        }
    }

    private List<ToolDefinition> GetRelevantTools(List<ToolDefinition> allTools, string currentMessage, List<ConversationMessage> history)
    {
        var relevantTools = new List<ToolDefinition>();
        var message = currentMessage.ToLower();

        // Keyword-based tool relevance
        var toolKeywords = new Dictionary<string, string[]>
        {
            ["file"] = new[] { "file", "read", "write", "directory", "folder", "document", "text" },
            ["web"] = new[] { "web", "http", "url", "website", "fetch", "download", "search" },
            ["system"] = new[] { "command", "execute", "run", "system", "process", "shell" },
            ["data"] = new[] { "data", "json", "csv", "database", "analyze", "parse" }
        };

        foreach (var tool in allTools)
        {
            var toolName = tool.Name.ToLower();
            var toolDesc = tool.Description?.ToLower() ?? "";

            // Direct name match
            if (message.Contains(toolName))
            {
                relevantTools.Add(tool);
                continue;
            }

            // Keyword matching
            foreach (var category in toolKeywords)
            {
                if (category.Value.Any(keyword => 
                    message.Contains(keyword) && (toolName.Contains(category.Key) || toolDesc.Contains(category.Key))))
                {
                    relevantTools.Add(tool);
                    break;
                }
            }
        }

        // If no specific tools found, include commonly used ones
        if (relevantTools.Count == 0)
        {
            relevantTools = GetEssentialTools(allTools, currentMessage);
        }

        // Limit to prevent token overflow
        return relevantTools.Take(10).ToList();
    }

    private List<ToolDefinition> GetEssentialTools(List<ToolDefinition> allTools, string currentMessage)
    {
        // Essential tools that are commonly useful
        var essentialPatterns = new[] { "list", "read", "help", "info" };
        
        return allTools
            .Where(t => essentialPatterns.Any(pattern => 
                t.Name.ToLower().Contains(pattern)))
            .Take(5)
            .ToList();
    }

    private List<ConversationMessage> CompressRecentMessages(List<ConversationMessage> history, int tokenBudget)
    {
        var compressed = new List<ConversationMessage>();
        var currentTokens = 0;

        for (int i = history.Count - 1; i >= 0; i--)
        {
            var message = history[i];
            var messageTokens = _tokenCounter.CountTokens(message.Content);

            if (currentTokens + messageTokens <= tokenBudget)
            {
                compressed.Insert(0, message);
                currentTokens += messageTokens;
            }
            else if (compressed.Count == 0)
            {
                // First message - truncate if needed
                compressed.Add(TruncateMessage(message, tokenBudget));
                break;
            }
            else
            {
                break;
            }
        }

        return compressed;
    }

    private ConversationMessage TruncateMessage(ConversationMessage message, int maxTokens)
    {
        var truncatedContent = TruncateText(message.Content, maxTokens - 20); // 20 token buffer
        
        return new ConversationMessage
        {
            Id = message.Id,
            Role = message.Role,
            Content = truncatedContent + " [truncated]",
            Timestamp = message.Timestamp,
            Metadata = message.Metadata
        };
    }

    private string TruncateText(string text, int maxTokens)
    {
        if (_tokenCounter.CountTokens(text) <= maxTokens)
            return text;

        // Rough truncation - could be improved with proper tokenization
        var words = text.Split(' ');
        var result = new StringBuilder();
        var currentTokens = 0;

        foreach (var word in words)
        {
            var wordTokens = _tokenCounter.CountTokens(word + " ");
            if (currentTokens + wordTokens > maxTokens)
                break;
                
            result.Append(word + " ");
            currentTokens += wordTokens;
        }

        return result.ToString().Trim();
    }

    private int EstimateToolsTokens(List<ToolDefinition> tools)
    {
        // Rough estimate for tool definitions in prompt
        return tools.Count * 50; // ~50 tokens per tool definition
    }
}