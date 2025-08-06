using McpAgent.Models;

namespace McpAgent.Services;

public interface ISystemContextProvider
{
    Task<SystemContext> GetCurrentContextAsync(CancellationToken cancellationToken = default);
    Task UpdateLocationAsync(LocationInfo location, CancellationToken cancellationToken = default);
    Task UpdateSystemMetricsAsync(SystemMetrics metrics, CancellationToken cancellationToken = default);
    Task<string> FormatContextForPromptAsync(ContextLevel level = ContextLevel.Standard);
    Task RefreshDynamicDataAsync();
}

public interface IPerformanceCounterService
{
    Task<SystemMetrics> GetCurrentMetricsAsync();
}

public interface ILocationService
{
    Task<LocationInfo?> GetApproximateLocationAsync();
    Task<bool> RequestLocationPermissionAsync();
    bool IsLocationEnabled { get; }
}

public interface IUserProfileService
{
    Task<UserProfile> GetCurrentProfileAsync(CancellationToken cancellationToken = default);
    Task UpdateProfileAsync(UserProfile profile, CancellationToken cancellationToken = default);
}