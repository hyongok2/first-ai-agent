using McpAgent.Domain.Entities;

namespace McpAgent.Domain.Interfaces;

public interface IDisplayProcess
{
    /// <summary>
    /// 진행 단계를 표시합니다.
    /// </summary>
    /// <param name="message"></param>
    void DisplayProcess(string message);
}