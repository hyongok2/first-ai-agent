using McpAgent.Domain.Entities;

namespace McpAgent.Domain.Interfaces;

/// <summary>
/// MCP 클라이언트 어댑터 인터페이스
/// </summary>
public interface IMcpClientAdapter
{
    /// <summary>
    /// 사용 가능한 도구들을 가져옵니다.
    /// </summary>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>사용 가능한 도구 목록</returns>
    Task<IReadOnlyList<ToolDefinition>> GetAvailableToolsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 지정된 도구를 실행합니다.
    /// </summary>
    /// <param name="toolName">도구 이름</param>
    /// <param name="arguments">도구 실행 인자</param>
    /// <param name="cancellationToken">취소 토큰</param>
    /// <returns>도구 실행 결과</returns>
    Task<object?> CallToolAsync(string toolName, Dictionary<string, object> arguments, CancellationToken cancellationToken = default);

    /// <summary>
    /// 연결된 서버 목록을 가져옵니다.
    /// </summary>
    /// <returns>연결된 서버 이름 목록</returns>
    Task<IReadOnlyList<string>> GetConnectedServersAsync();

    /// <summary>
    /// MCP 클라이언트를 초기화합니다.
    /// </summary>
    /// <param name="cancellationToken">취소 토큰</param>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// MCP 클라이언트를 종료합니다.
    /// </summary>
    /// <param name="cancellationToken">취소 토큰</param>
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}