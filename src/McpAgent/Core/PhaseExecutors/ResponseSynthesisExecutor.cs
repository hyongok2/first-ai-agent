using System.Text.Json;
using McpAgent.Models;
using McpAgent.Providers;
using McpAgent.Services;
using Microsoft.Extensions.Logging;

namespace McpAgent.Core.PhaseExecutors;

public class ResponseSynthesisExecutor : IPhaseExecutor
{
    private readonly ILogger<ResponseSynthesisExecutor> _logger;
    private readonly ILlmProvider _llm;
    private readonly ISystemContextProvider _contextProvider;
    private readonly IDebugFileLogger _debugLogger;
    private readonly ITokenCalculationService _tokenCalculation;
    
    public int PhaseNumber => 5;
    
    public ResponseSynthesisExecutor(
        ILogger<ResponseSynthesisExecutor> logger,
        ILlmProvider llm,
        ISystemContextProvider contextProvider,
        IDebugFileLogger debugLogger,
        ITokenCalculationService tokenCalculation)
    {
        _logger = logger;
        _llm = llm;
        _contextProvider = contextProvider;
        _debugLogger = debugLogger;
        _tokenCalculation = tokenCalculation;
    }
    
    public async Task<PhaseResult> ExecuteAsync(ConversationState state, string userInput, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!state.PhaseHistory.TryGetValue(4, out var executionResult))
            {
                return CreateErrorResult("Tool execution result not found");
            }
            
            // 실행 결과가 있는지 확인
            var executionResults = ExtractExecutionResults(executionResult);
            if (!executionResults.Any())
            {
                return CreateSimpleResponse("I was unable to process your request properly.");
            }
            
            var systemContext = await _contextProvider.FormatContextForPromptAsync(ContextLevel.Minimal);
            var prompt = await BuildResponseSynthesisPromptAsync(systemContext, userInput, state, executionResults, cancellationToken);
            
            _logger.LogDebug("Phase 5: Synthesizing final response");
            var response = await _llm.GenerateResponseAsync(prompt, [], cancellationToken);
            
            // Debug logging for prompt and response
            await _debugLogger.LogPromptAndResponseAsync(prompt, response, "response-synthesis");
            
            var parsed = ParseResponseSynthesis(response);
            
            return new PhaseResult
            {
                Phase = 5,
                Status = ExecutionStatus.Success,
                Data = parsed.ToDictionary(),
                ConfidenceScore = 1.0,
                RequiresUserInput = parsed.ConversationStatus == "awaiting_input"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in response synthesis phase");
            return CreateSimpleResponse("I encountered an error while processing your request. Please try again.");
        }
    }
    
    private async Task<string> BuildResponseSynthesisPromptAsync(string systemContext, string userInput, ConversationState state, List<object> executionResults, CancellationToken cancellationToken)
    {
        var resultsJson = JsonSerializer.Serialize(executionResults, new JsonSerializerOptions { WriteIndented = true });
        
        // 이전 단계들의 컨텍스트 수집
        var intentData = state.PhaseHistory.GetValueOrDefault(1)?.Data ?? new Dictionary<string, object>();
        var functionData = state.PhaseHistory.GetValueOrDefault(2)?.Data ?? new Dictionary<string, object>();
        
        // LLM 기반 대화 히스토리 요약 생성
        var conversationHistory = await GenerateConversationSummaryAsync(state, cancellationToken);
        
        var contextSummary = JsonSerializer.Serialize(new
        {
            original_intent = intentData.GetValueOrDefault("intent_type"),
            selected_function = functionData.GetValueOrDefault("primary_function"),
            execution_summary = $"{executionResults.Count} tool(s) executed",
            conversation_history = conversationHistory
        });
        
        return $@"
{systemContext}

**ROLE**: Response Synthesizer
**TASK**: Create natural, helpful response from execution results

**ORIGINAL USER INPUT**: {userInput}
**PROCESSING CONTEXT**: {contextSummary}
**CONVERSATION HISTORY**: {conversationHistory}
**EXECUTION RESULTS**: {resultsJson}

**MCP RESPONSE STRUCTURE UNDERSTANDING**:
- Each execution result has a 'result' object with the actual MCP tool response
- Look for 'text' field in result - this contains the main readable content
- Look for 'success' field to determine if the operation succeeded
- Look for 'error' field if the operation failed
- 'content' array may contain structured data with 'type' and 'text' fields

**RESPONSE GUIDELINES**:
1. Extract the main content from 'text' field or 'content[].text' in results
2. Create a natural, conversational response based on actual tool outputs
3. Present file contents, directory listings, or other data in readable format
4. Summarize what was accomplished using the actual tool results
5. Consider conversation history context when crafting the response
6. Generate a comprehensive summary of this conversation for future reference
7. Suggest logical follow-up actions if appropriate
8. Be concise but informative, focusing on the actual data returned
9. If errors occurred, explain what went wrong using the error messages from tools

**SPECIAL CASES**:
- If chat_response was executed: Use the result text directly with minor enhancement
- If file operations: Present the actual file content or directory listing from 'text' field
- If errors occurred: Extract error message from 'error' field and provide helpful guidance
- If multiple tools used: Summarize the workflow and combine all text results logically
- If 'text' field is empty but 'content' exists: Extract text from content array

**RESPONSE FORMAT** (JSON only):
{{
    ""natural_response"": ""User-friendly response explaining results"",
    ""follow_up_suggestions"": [
        ""suggestion 1"",
        ""suggestion 2""
    ],
    ""conversation_status"": ""complete|awaiting_input|continue_task"",
    ""next_phase_hint"": 1,
    ""summary"": ""Brief summary of what was accomplished""
}}";
    }
    
    private List<object> ExtractExecutionResults(PhaseResult executionResult)
    {
        if (executionResult.Data.TryGetValue("execution_results", out var resultsObj) &&
            resultsObj is List<object> resultsList)
        {
            return resultsList;
        }
        
        return new List<object>();
    }
    
    private ResponseSynthesisResult ParseResponseSynthesis(string response)
    {
        try
        {
            var cleanResponse = ExtractJsonFromResponse(response);
            var parsed = JsonSerializer.Deserialize<JsonElement>(cleanResponse);
            
            var result = new ResponseSynthesisResult
            {
                NaturalResponse = parsed.TryGetProperty("natural_response", out var naturalResp) ? 
                    naturalResp.GetString() ?? response : response,
                ConversationStatus = parsed.TryGetProperty("conversation_status", out var status) ? 
                    status.GetString() ?? "complete" : "complete",
                Summary = parsed.TryGetProperty("summary", out var summary) ? 
                    summary.GetString() ?? "" : ""
            };
            
            // Follow-up suggestions 파싱
            if (parsed.TryGetProperty("follow_up_suggestions", out var suggestions))
            {
                result.FollowUpSuggestions = suggestions.EnumerateArray()
                    .Select(s => s.GetString() ?? "")
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
            }
            
            // Next phase hint 파싱
            if (parsed.TryGetProperty("next_phase_hint", out var nextPhase))
            {
                result.NextPhaseHint = nextPhase.GetInt32();
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse response synthesis, using raw response");
            
            // 파싱 실패시 원본 응답 사용
            return new ResponseSynthesisResult
            {
                NaturalResponse = response,
                ConversationStatus = "complete",
                Summary = "Response generated"
            };
        }
    }
    
    private string ExtractJsonFromResponse(string response)
    {
        var startIndex = response.IndexOf('{');
        var endIndex = response.LastIndexOf('}');
        
        if (startIndex >= 0 && endIndex > startIndex)
        {
            return response.Substring(startIndex, endIndex - startIndex + 1);
        }
        
        return response;
    }
    
    /// <summary>
    /// LLM을 사용하여 대화 히스토리를 자연스럽게 요약
    /// </summary>
    private async Task<string> GenerateConversationSummaryAsync(ConversationState state, CancellationToken cancellationToken)
    {
        try
        {
            // 요약할 내용이 없으면 빈 문자열 반환
            if (!state.UserContext.RecentQueries.Any() && !state.PhaseHistory.Any())
            {
                return "No previous conversation context";
            }
            
            // 요약을 위한 데이터 수집
            var summaryData = CollectSummaryData(state);
            
            // 요약이 필요한 정도의 데이터가 없으면 간단한 정보만 반환
            if (string.IsNullOrWhiteSpace(summaryData))
            {
                return "Starting new conversation";
            }
            
            var summaryPrompt = BuildSummaryPrompt(summaryData);
            
            _logger.LogDebug("Generating conversation summary using LLM");
            var summary = await _llm.GenerateResponseAsync(summaryPrompt, [], cancellationToken);
            
            // Debug logging for summary generation
            await _debugLogger.LogPromptAndResponseAsync(summaryPrompt, summary, "conversation-summary");
            
            // 요약 결과를 정리하고 반환
            var cleanSummary = ExtractSummaryFromResponse(summary);
            return !string.IsNullOrWhiteSpace(cleanSummary) ? cleanSummary : "Previous conversation completed";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate conversation summary, using fallback");
            return BuildFallbackSummary(state);
        }
    }
    
    /// <summary>
    /// 토큰 기반 적응형 요약 데이터 수집
    /// </summary>
    private string CollectSummaryData(ConversationState state)
    {
        var dataPoints = new List<string>();
        
        // 토큰 예산 계산
        var availableTokens = _tokenCalculation.GetAvailableTokensForHistory();
        var currentTokenUsage = 0;
        var maxHistoryTokens = (int)(availableTokens * 0.3); // 히스토리는 전체 컨텍스트의 30%만 사용
        
        _logger.LogDebug("Token budget for history: {MaxTokens}/{Available} tokens", maxHistoryTokens, availableTokens);
        
        // 1. 메타 요약 우선 추가 (가장 압축된 정보)
        if (!string.IsNullOrEmpty(state.UserContext.MetaSummary))
        {
            var metaSummaryText = $"Previous session history: {state.UserContext.MetaSummary}";
            var metaTokens = _tokenCalculation.EstimateTokenCount(metaSummaryText);
            
            if (currentTokenUsage + metaTokens <= maxHistoryTokens)
            {
                dataPoints.Add(metaSummaryText);
                currentTokenUsage += metaTokens;
                _logger.LogDebug("Added meta summary: {Tokens} tokens", metaTokens);
            }
            else
            {
                // 메타 요약도 너무 크면 압축
                var compressedMeta = _tokenCalculation.CompressToTokenLimit(metaSummaryText, maxHistoryTokens / 2);
                dataPoints.Add(compressedMeta);
                currentTokenUsage += _tokenCalculation.EstimateTokenCount(compressedMeta);
                _logger.LogDebug("Added compressed meta summary: {Tokens} tokens", currentTokenUsage);
            }
        }
        
        // 2. 개별 대화 요약들을 토큰 예산에 맞게 선택
        if (state.UserContext.ConversationSummaries.Any())
        {
            var remainingTokens = maxHistoryTokens - currentTokenUsage;
            var summaryTexts = new List<string>();
            
            // 최신 대화부터 역순으로 추가 (더 중요함)
            var sortedSummaries = state.UserContext.ConversationSummaries.OrderByDescending(s => s.CreatedAt).ToList();
            
            foreach (var summary in sortedSummaries)
            {
                var summaryText = BuildSummaryText(summary);
                var summaryTokens = _tokenCalculation.EstimateTokenCount(summaryText);
                
                if (currentTokenUsage + summaryTokens <= maxHistoryTokens)
                {
                    summaryTexts.Insert(0, summaryText); // 원래 순서로 유지
                    currentTokenUsage += summaryTokens;
                }
                else
                {
                    // 토큰 제한에 도달
                    break;
                }
            }
            
            if (summaryTexts.Any())
            {
                dataPoints.Add($"Recent conversations: {string.Join("; ", summaryTexts)}");
                _logger.LogDebug("Added {Count} individual summaries: {Tokens} tokens", 
                    summaryTexts.Count, _tokenCalculation.EstimateTokenCount(string.Join("; ", summaryTexts)));
            }
        }
        
        // 3. 백업용 최근 쿼리들 (다른 데이터가 없을 때만)
        if (!dataPoints.Any() && state.UserContext.RecentQueries.Any())
        {
            var recentQueries = _tokenCalculation.FilterByTokenLimit(
                state.UserContext.RecentQueries.ToList(), maxHistoryTokens);
                
            if (recentQueries.Any())
            {
                var queriesText = $"Recent user requests: {string.Join(", ", recentQueries)}";
                dataPoints.Add(queriesText);
                currentTokenUsage += _tokenCalculation.EstimateTokenCount(queriesText);
                _logger.LogDebug("Added fallback queries: {Tokens} tokens", _tokenCalculation.EstimateTokenCount(queriesText));
            }
        }
        
        // 현재 대화의 주요 단계별 정보 (도구 실행 결과 포함)
        if (state.PhaseHistory.Any())
        {
            var phases = new List<string>();
            foreach (var phase in state.PhaseHistory.OrderBy(p => p.Key))
            {
                var phaseData = phase.Value.Data;
                var phaseInfo = phase.Key switch
                {
                    1 => $"User intent was identified as: {phaseData.GetValueOrDefault("intent_type", "unknown")}",
                    2 => $"Selected function: {phaseData.GetValueOrDefault("primary_function", "unknown")}",
                    4 when phaseData.ContainsKey("execution_results") => ExtractToolExecutionSummary(phaseData),
                    4 => "Operation execution failed",
                    _ => null
                };
                
                if (phaseInfo != null)
                    phases.Add(phaseInfo);
            }
            
            if (phases.Any())
                dataPoints.Add($"Conversation flow: {string.Join("; ", phases)}");
        }
        
        // 현재 진행 중인 태스크
        if (!string.IsNullOrEmpty(state.UserContext.CurrentTask))
        {
            dataPoints.Add($"Current task context: {state.UserContext.CurrentTask}");
        }
        
        var finalResult = string.Join("\n", dataPoints);
        var finalTokens = _tokenCalculation.EstimateTokenCount(finalResult);
        
        _logger.LogDebug("Final summary data: {Tokens} tokens, {DataPoints} sections", finalTokens, dataPoints.Count);
        
        // 최종 토큰 체크 및 압축
        if (finalTokens > maxHistoryTokens)
        {
            _logger.LogWarning("Summary data exceeds token limit ({Tokens} > {MaxTokens}), applying final compression", 
                finalTokens, maxHistoryTokens);
            
            finalResult = _tokenCalculation.CompressToTokenLimit(finalResult, maxHistoryTokens);
        }
        
        return finalResult;
    }
    
    /// <summary>
    /// ConversationSummary를 텍스트로 변환
    /// </summary>
    private string BuildSummaryText(ConversationSummary summary)
    {
        var text = $"{summary.UserRequest} → {summary.Summary}";
        
        // 중요한 결과 데이터가 있으면 추가 (최대 2개)
        if (summary.KeyResults.Any())
        {
            var keyResults = summary.KeyResults.Values.Take(2).ToList();
            text += $" (Data: {string.Join(", ", keyResults)})";
        }
        
        return text;
    }
    
    /// <summary>
    /// 도구 실행 결과에서 핵심 정보를 추출하여 요약 생성
    /// </summary>
    private string ExtractToolExecutionSummary(Dictionary<string, object> phaseData)
    {
        try
        {
            if (phaseData.TryGetValue("execution_results", out var resultsObj) && 
                resultsObj is List<object> resultsList && resultsList.Any())
            {
                var summaryParts = new List<string>();
                
                foreach (var result in resultsList)
                {
                    var resultSummary = ExtractSingleToolResult(result);
                    if (!string.IsNullOrWhiteSpace(resultSummary))
                        summaryParts.Add(resultSummary);
                }
                
                if (summaryParts.Any())
                {
                    var toolsUsed = summaryParts.Count;
                    var combinedSummary = string.Join("; ", summaryParts);
                    return $"Executed {toolsUsed} tool(s) successfully: {combinedSummary}";
                }
            }
            
            return "Successfully executed requested operation";
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error extracting tool execution summary");
            return "Successfully executed requested operation";
        }
    }
    
    /// <summary>
    /// 단일 도구 실행 결과에서 핵심 정보 추출
    /// </summary>
    private string ExtractSingleToolResult(object result)
    {
        try
        {
            if (result == null) return "";
            
            // JSON 요소로 변환 시도
            var jsonElement = result switch
            {
                JsonElement element => element,
                string jsonString when !string.IsNullOrWhiteSpace(jsonString) => 
                    JsonSerializer.Deserialize<JsonElement>(jsonString),
                _ => JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result))
            };
            
            // 도구별 결과 추출 전략
            var toolName = ExtractToolName(jsonElement);
            
            return toolName switch
            {
                // Oracle DB 도구들
                "OracleDbTools_TestConnection" => ExtractConnectionTestResult(jsonElement),
                "OracleDbTools_GetDatabaseInfo" => ExtractDatabaseInfoResult(jsonElement),
                "OracleDbTools_Query" => ExtractQueryResult(jsonElement),
                
                // 파일 시스템 도구들
                var name when name.Contains("File") || name.Contains("Directory") => ExtractFileSystemResult(jsonElement),
                
                // Echo 도구
                "Echo_Echo" => ExtractEchoResult(jsonElement),
                
                // 기본 처리
                _ => ExtractGenericResult(jsonElement)
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error extracting single tool result");
            return "";
        }
    }
    
    /// <summary>
    /// 도구 이름 추출
    /// </summary>
    private string ExtractToolName(JsonElement result)
    {
        if (result.TryGetProperty("tool_name", out var toolNameProp))
            return toolNameProp.GetString() ?? "";
            
        if (result.TryGetProperty("function_name", out var funcNameProp))
            return funcNameProp.GetString() ?? "";
            
        return "";
    }
    
    /// <summary>
    /// Oracle DB 연결 테스트 결과 추출
    /// </summary>
    private string ExtractConnectionTestResult(JsonElement result)
    {
        if (result.TryGetProperty("success", out var success) && success.GetBoolean())
        {
            return "Oracle DB connection successful";
        }
        
        if (result.TryGetProperty("text", out var text))
        {
            var textContent = text.GetString() ?? "";
            if (textContent.Contains("successful", StringComparison.OrdinalIgnoreCase))
                return "Oracle DB connection successful";
        }
        
        return "Oracle DB connection tested";
    }
    
    /// <summary>
    /// Oracle DB 정보 조회 결과 추출
    /// </summary>
    private string ExtractDatabaseInfoResult(JsonElement result)
    {
        if (result.TryGetProperty("text", out var text))
        {
            var textContent = text.GetString() ?? "";
            
            // 테이블 개수 추출
            var tableCount = ExtractNumberFromText(textContent, @"(\d+)\s*table", "tables found");
            
            // 주요 테이블 이름 추출
            var importantTables = ExtractTableNames(textContent);
            
            var summary = "Retrieved Oracle DB schema";
            if (tableCount > 0)
                summary += $" ({tableCount} tables)";
            if (importantTables.Any())
                summary += $" including {string.Join(", ", importantTables)}";
                
            return summary;
        }
        
        return "Retrieved Oracle DB schema information";
    }
    
    /// <summary>
    /// SQL 쿼리 실행 결과 추출
    /// </summary>
    private string ExtractQueryResult(JsonElement result)
    {
        if (result.TryGetProperty("text", out var text))
        {
            var textContent = text.GetString() ?? "";
            var rowCount = ExtractNumberFromText(textContent, @"(\d+)\s*row", "rows returned");
            
            if (rowCount > 0)
                return $"Query executed, {rowCount} rows returned";
        }
        
        return "SQL query executed successfully";
    }
    
    /// <summary>
    /// 파일 시스템 작업 결과 추출
    /// </summary>
    private string ExtractFileSystemResult(JsonElement result)
    {
        if (result.TryGetProperty("text", out var text))
        {
            var textContent = text.GetString() ?? "";
            
            if (textContent.Contains("directory", StringComparison.OrdinalIgnoreCase))
            {
                var fileCount = ExtractNumberFromText(textContent, @"(\d+)\s*file", "files found");
                return fileCount > 0 ? $"Directory listing: {fileCount} files" : "Directory listing completed";
            }
            
            if (textContent.Length > 100)
                return $"File content retrieved ({textContent.Length} characters)";
        }
        
        return "File system operation completed";
    }
    
    /// <summary>
    /// Echo 도구 결과 추출
    /// </summary>
    private string ExtractEchoResult(JsonElement result)
    {
        if (result.TryGetProperty("text", out var text))
        {
            var textContent = text.GetString() ?? "";
            return $"Echo: {textContent.Substring(0, Math.Min(50, textContent.Length))}";
        }
        
        return "Echo executed";
    }
    
    /// <summary>
    /// 일반적인 결과 추출
    /// </summary>
    private string ExtractGenericResult(JsonElement result)
    {
        if (result.TryGetProperty("success", out var success) && !success.GetBoolean())
        {
            if (result.TryGetProperty("error", out var error))
            {
                return $"Operation failed: {error.GetString()}";
            }
            return "Operation failed";
        }
        
        if (result.TryGetProperty("text", out var text))
        {
            var textContent = text.GetString() ?? "";
            if (textContent.Length > 100)
                return $"Operation completed with data ({textContent.Length} characters)";
            else if (!string.IsNullOrWhiteSpace(textContent))
                return $"Operation completed: {textContent.Substring(0, Math.Min(50, textContent.Length))}";
        }
        
        return "Operation completed successfully";
    }
    
    /// <summary>
    /// 텍스트에서 숫자 추출 헬퍼
    /// </summary>
    private int ExtractNumberFromText(string text, string pattern, string fallbackIndicator)
    {
        try
        {
            var match = System.Text.RegularExpressions.Regex.Match(text, pattern, 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            if (match.Success && int.TryParse(match.Groups[1].Value, out var number))
                return number;
                
            // Fallback: 텍스트에서 일반적인 숫자 찾기
            if (text.Contains(fallbackIndicator, StringComparison.OrdinalIgnoreCase))
            {
                var numbers = System.Text.RegularExpressions.Regex.Matches(text, @"\d+");
                if (numbers.Count > 0 && int.TryParse(numbers[0].Value, out var fallbackNumber))
                    return fallbackNumber;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error extracting number from text");
        }
        
        return 0;
    }
    
    /// <summary>
    /// 텍스트에서 중요 테이블 이름 추출
    /// </summary>
    private List<string> ExtractTableNames(string text)
    {
        var importantTables = new List<string>();
        var commonImportantNames = new[] { "user", "users", "product", "products", "order", "orders", "customer", "customers" };
        
        foreach (var name in commonImportantNames)
        {
            if (text.Contains(name, StringComparison.OrdinalIgnoreCase) && !importantTables.Contains(name.ToUpper()))
            {
                importantTables.Add(name.ToUpper());
            }
        }
        
        return importantTables.Take(3).ToList(); // 최대 3개만
    }
    
    /// <summary>
    /// 대화 요약을 위한 프롬프트 생성
    /// </summary>
    private string BuildSummaryPrompt(string summaryData)
    {
        return $@"
**ROLE**: Conversation Summarizer
**TASK**: Create a natural, concise summary of the conversation history for context in future interactions

**CONVERSATION DATA TO SUMMARIZE**:
{summaryData}

**SUMMARY GUIDELINES**:
1. Create a natural, conversational summary in 1-2 sentences
2. Focus on what the user accomplished and the context established
3. **Include specific results and data discovered** (table counts, file contents, query results, etc.)
4. Mention key tools used and their concrete outcomes
5. Keep it concise but informative for future conversation context
6. Write in a way that helps understand the user's ongoing needs and data context
7. Use natural language, avoid technical jargon where possible
8. **Prioritize actionable data that could be useful for follow-up requests**

**EXAMPLES OF GOOD SUMMARIES WITH SPECIFIC DATA**:
- ""Connected to Oracle database and retrieved schema with 8 tables including USER, PRODUCT, and ORDER tables for analysis""
- ""Executed SQL query returning 150 user records with email and profile data for the reporting project""
- ""Listed project directory containing 25 files including config.json and database scripts for deployment setup""
- ""Tested Oracle connection successfully and discovered customer database with 12 tables containing sales transaction data""

**RESPONSE FORMAT**: 
Provide only the summary text, no JSON or additional formatting.";
    }
    
    /// <summary>
    /// LLM 응답에서 요약 텍스트 추출
    /// </summary>
    private string ExtractSummaryFromResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return "";
            
        // 불필요한 접두사나 서식 제거
        var cleaned = response.Trim()
            .Replace("Summary:", "")
            .Replace("**Summary**:", "")
            .Replace("SUMMARY:", "")
            .Trim();
            
        // 첫 번째 문단만 사용 (요약은 짧게)
        var lines = cleaned.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return lines.FirstOrDefault()?.Trim() ?? "";
    }
    
    /// <summary>
    /// LLM 요약 실패 시 사용할 간단한 폴백 요약
    /// </summary>
    private string BuildFallbackSummary(ConversationState state)
    {
        if (state.UserContext.RecentQueries.Any())
        {
            var lastQuery = state.UserContext.RecentQueries.LastOrDefault();
            return $"Previously: {lastQuery}";
        }
        
        return "Previous conversation completed";
    }
    
    private PhaseResult CreateErrorResult(string message)
    {
        return new PhaseResult
        {
            Phase = 5,
            Status = ExecutionStatus.Failure,
            ErrorMessage = message,
            Data = new Dictionary<string, object> { ["natural_response"] = message }
        };
    }
    
    private PhaseResult CreateSimpleResponse(string message)
    {
        return new PhaseResult
        {
            Phase = 5,
            Status = ExecutionStatus.Success,
            Data = new Dictionary<string, object>
            {
                ["natural_response"] = message,
                ["conversation_status"] = "complete",
                ["summary"] = "Simple response generated"
            },
            ConfidenceScore = 0.8
        };
    }
}

public class ResponseSynthesisResult
{
    public string NaturalResponse { get; set; } = string.Empty;
    public List<string> FollowUpSuggestions { get; set; } = new();
    public string ConversationStatus { get; set; } = "complete";
    public int NextPhaseHint { get; set; } = 1;
    public string Summary { get; set; } = string.Empty;
    
    public Dictionary<string, object> ToDictionary()
    {
        var dict = new Dictionary<string, object>
        {
            ["natural_response"] = NaturalResponse,
            ["follow_up_suggestions"] = FollowUpSuggestions,
            ["conversation_status"] = ConversationStatus,
            ["summary"] = Summary
        };
        
        if (ConversationStatus == "continue_task")
        {
            dict["next_phase"] = NextPhaseHint;
        }
        
        return dict;
    }
}