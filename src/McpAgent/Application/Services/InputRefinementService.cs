using System.Text.Json;
using McpAgent.Domain.Entities;
using McpAgent.Domain.Interfaces;
using McpAgent.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace McpAgent.Application.Services;

public class InputRefinementService : IInputRefinementService
{
    private readonly ILlmProvider _llmProvider;
    private readonly IPromptService _promptService;
    private readonly ILogger<InputRefinementService> _logger;

    public InputRefinementService(
        ILlmProvider llmProvider,
        IPromptService promptService,
        ILogger<InputRefinementService> logger)
    {
        _llmProvider = llmProvider;
        _promptService = promptService;
        _logger = logger;
    }

    public async Task<RefinedInput> RefineInputAsync(
        string originalInput,
        IReadOnlyList<ConversationMessage> conversationHistory,
        string systemContext,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting input refinement for: {Input}", originalInput);

            // Load the input refinement prompt template
            var promptTemplate = await _promptService.GetPromptAsync("input-refinement");
            
            // Convert conversation history to string format
            var conversationHistoryText = FormatConversationHistory(conversationHistory);
            
            // Replace placeholders in the template
            var prompt = promptTemplate
                .Replace("{SYSTEM_CONTEXT}", systemContext)
                .Replace("{CONVERSATION_HISTORY}", conversationHistoryText)
                .Replace("{USER_INPUT}", originalInput);

            // Call LLM to refine the input
            var response = await _llmProvider.GenerateResponseAsync(prompt, cancellationToken);
            
            // Parse the JSON response
            var refinementResult = ParseRefinementResult(response);
            
            _logger.LogInformation("Input refinement completed successfully");
            
            return refinementResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refine input: {Input}", originalInput);
            
            // Return fallback refined input on error
            return CreateFallbackRefinedInput(originalInput);
        }
    }

    private RefinedInput ParseRefinementResult(string response)
    {
        try
        {
            // Extract JSON from response if it's wrapped in markdown
            var jsonResponse = ExtractJsonFromResponse(response);
            
            var jsonDocument = JsonDocument.Parse(jsonResponse);
            var root = jsonDocument.RootElement;

            var originalInput = root.TryGetProperty("original_input", out var origInput) 
                ? origInput.GetString() ?? "" : "";
            var clarifiedIntent = root.TryGetProperty("clarified_intent", out var intent) 
                ? intent.GetString() ?? "" : "";
            var refinedQuery = root.TryGetProperty("refined_query", out var query) 
                ? query.GetString() ?? "" : "";
            var extractedEntities = ParseEntities(root);
            var context = ParseContext(root);
            var confidenceLevel = DetermineConfidenceLevel(root);

            return new RefinedInput(
                originalInput,
                clarifiedIntent,
                refinedQuery,
                extractedEntities, // List<string>
                context,
                null, // suggestedPlan
                confidenceLevel
            );
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse refinement result JSON: {Response}", response);
            throw new InvalidOperationException("Failed to parse LLM response as JSON", ex);
        }
    }

    private List<string> ParseEntities(JsonElement root)
    {
        var entities = new List<string>();
        
        if (root.TryGetProperty("extracted_entities", out var entitiesElement))
        {
            if (entitiesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in entitiesElement.EnumerateArray())
                {
                    var entity = item.GetString();
                    if (!string.IsNullOrEmpty(entity))
                    {
                        entities.Add(entity);
                    }
                }
            }
            else if (entitiesElement.ValueKind == JsonValueKind.Object)
            {
                // If it's an object, treat property names as entities
                foreach (var property in entitiesElement.EnumerateObject())
                {
                    entities.Add(property.Name);
                }
            }
        }
        
        return entities;
    }

    private List<string> ParseFollowUpQuestions(JsonElement root)
    {
        var questions = new List<string>();
        
        if (root.TryGetProperty("follow_up_questions", out var questionsElement) &&
            questionsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var question in questionsElement.EnumerateArray())
            {
                var questionText = question.GetString();
                if (!string.IsNullOrEmpty(questionText))
                {
                    questions.Add(questionText);
                }
            }
        }
        
        return questions;
    }

    private string ExtractJsonFromResponse(string response)
    {
        // Remove markdown code blocks if present
        var lines = response.Split('\n');
        var jsonStartIndex = -1;
        var jsonEndIndex = -1;

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim().StartsWith("```json"))
            {
                jsonStartIndex = i + 1;
            }
            else if (lines[i].Trim() == "```" && jsonStartIndex != -1)
            {
                jsonEndIndex = i;
                break;
            }
        }

        if (jsonStartIndex != -1 && jsonEndIndex != -1)
        {
            return string.Join('\n', lines[jsonStartIndex..jsonEndIndex]);
        }

        // If no markdown blocks found, assume entire response is JSON
        return response.Trim();
    }

    private RefinedInput CreateFallbackRefinedInput(string originalInput)
    {
        _logger.LogWarning("Creating fallback refined input for: {Input}", originalInput);
        
        return new RefinedInput(
            originalInput,
            originalInput, // Use original as clarified intent for fallback
            originalInput, // Use original as refined query for fallback
            new List<string>(), // Empty extracted entities
            new Dictionary<string, object>(), // Empty context
            "요청을 더 구체적으로 설명해 주세요", // Suggested plan
            ConfidenceLevel.Low // Low confidence for fallback
        );
    }

    private Dictionary<string, object> ParseContext(JsonElement root)
    {
        var context = new Dictionary<string, object>();
        if (root.TryGetProperty("context", out var contextElement))
        {
            foreach (var property in contextElement.EnumerateObject())
            {
                var value = (object)(property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString() ?? "",
                    JsonValueKind.Number => property.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => property.Value.GetRawText()
                });
                context[property.Name] = value;
            }
        }
        return context;
    }

    private ConfidenceLevel DetermineConfidenceLevel(JsonElement root)
    {
        if (root.TryGetProperty("confidence_level", out var confidence))
        {
            var confidenceValue = confidence.GetDouble();
            return confidenceValue switch
            {
                >= 0.8 => ConfidenceLevel.VeryHigh,
                >= 0.6 => ConfidenceLevel.High,
                >= 0.4 => ConfidenceLevel.Medium,
                >= 0.2 => ConfidenceLevel.Low,
                _ => ConfidenceLevel.VeryLow
            };
        }
        return ConfidenceLevel.Medium;
    }

    private string FormatConversationHistory(IReadOnlyList<ConversationMessage> conversationHistory)
    {
        if (conversationHistory == null || conversationHistory.Count == 0)
        {
            return "대화 이력이 없습니다.";
        }

        var history = conversationHistory
            .Select(msg => $"{msg.Role}: {msg.Content}")
            .ToList();

        return string.Join('\n', history);
    }
}