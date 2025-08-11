using McpAgent.Application.Interfaces;
using McpAgent.Configuration;
using McpAgent.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace McpAgent.Infrastructure.MCP;

/// <summary>
/// MCP 클라이언트 팩토리 - 서버 타입에 따라 적절한 클라이언트를 생성
/// </summary>
public interface IMcpClientFactory
{
    /// <summary>
    /// 서버 설정에 따라 MCP 클라이언트를 생성합니다.
    /// </summary>
    /// <param name="serverConfig">서버 설정</param>
    /// <returns>MCP 클라이언트 어댑터</returns>
    IMcpClientAdapter CreateClient(McpServerConfig serverConfig);
    
    /// <summary>
    /// 모든 설정된 서버에 대한 클라이언트를 생성합니다.
    /// </summary>
    /// <param name="mcpConfig">MCP 설정</param>
    /// <returns>MCP 클라이언트 어댑터 목록</returns>
    IReadOnlyList<IMcpClientAdapter> CreateClients(McpConfiguration mcpConfig);
}

public class McpClientFactory : IMcpClientFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IRequestResponseLogger? _requestResponseLogger;

    public McpClientFactory(
        ILoggerFactory loggerFactory,
        IHttpClientFactory httpClientFactory,
        IRequestResponseLogger? requestResponseLogger = null)
    {
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _requestResponseLogger = requestResponseLogger;
    }

    public IMcpClientAdapter CreateClient(McpServerConfig serverConfig)
    {
        if (serverConfig == null)
        {
            throw new ArgumentNullException(nameof(serverConfig));
        }

        var clientLogger = _loggerFactory.CreateLogger<HttpMcpClientAdapter>();
        return new HttpMcpClientAdapter(clientLogger, serverConfig, _httpClientFactory, _requestResponseLogger);
    }

    public IReadOnlyList<IMcpClientAdapter> CreateClients(McpConfiguration mcpConfig)
    {
        if (mcpConfig == null || !mcpConfig.Enabled || mcpConfig.Servers == null)
        {
            return Array.Empty<IMcpClientAdapter>();
        }

        var clients = new List<IMcpClientAdapter>();

        foreach (var serverConfig in mcpConfig.Servers)
        {
            try
            {
                var client = CreateClient(serverConfig);
                clients.Add(client);
            }
            catch (Exception ex)
            {
                var logger = _loggerFactory.CreateLogger<McpClientFactory>();
                logger.LogError(ex, "Failed to create MCP client for server {ServerName}", serverConfig.Name);
            }
        }

        return clients;
    }
}