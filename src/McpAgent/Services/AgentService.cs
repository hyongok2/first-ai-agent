namespace McpAgent.Services;

[Obsolete("Use AgentHostedService instead")]
public class AgentService
{
    public Task RunAsync()
    {
        throw new NotImplementedException("This service is obsolete. Use AgentHostedService instead.");
    }
}