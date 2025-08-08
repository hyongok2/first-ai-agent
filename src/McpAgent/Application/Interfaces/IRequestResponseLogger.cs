namespace McpAgent.Application.Interfaces;

/// <summary>
/// 요청/응답 파일 로깅 인터페이스
/// </summary>
public interface IRequestResponseLogger
{
    /// <summary>
    /// LLM 요청/응답을 파일에 로깅
    /// </summary>
    Task LogLlmRequestResponseAsync(string model, string stage, string request, string response, double elapsedMilliseconds, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// MCP 요청/응답을 파일에 로깅
    /// </summary>
    Task LogMcpRequestResponseAsync(string mcpServer, string toolName, string request, string response, double elapsedMilliseconds, CancellationToken cancellationToken = default);
}