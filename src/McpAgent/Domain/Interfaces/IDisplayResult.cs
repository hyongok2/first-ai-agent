using McpAgent.Domain.Entities;

namespace McpAgent.Domain.Interfaces;

public interface IDisplayResult
{
    void DisplayError(string message);

    void DisplaySuccessMessage(string message);
}