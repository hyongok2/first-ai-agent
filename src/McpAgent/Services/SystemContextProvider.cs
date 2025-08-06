using System.Text;
using McpAgent.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace McpAgent.Services;

public class SystemContextProvider : ISystemContextProvider
{
    private readonly ILogger<SystemContextProvider> _logger;
    private readonly IUserProfileService _userProfile;
    private readonly IPerformanceCounterService _performanceCounter;
    private readonly ILocationService _locationService;
    private readonly Timer _refreshTimer;
    private SystemContext _currentContext;
    
    public SystemContextProvider(
        ILogger<SystemContextProvider> logger,
        IUserProfileService userProfile,
        IPerformanceCounterService performanceCounter,
        ILocationService locationService)
    {
        _logger = logger;
        _userProfile = userProfile;
        _performanceCounter = performanceCounter;
        _locationService = locationService;
        _currentContext = new SystemContext();
        
        // 동적 정보를 5분마다 갱신
        _refreshTimer = new Timer(async _ => await RefreshDynamicDataAsync(), 
            null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
    }
    
    public async Task<SystemContext> GetCurrentContextAsync(CancellationToken cancellationToken = default)
    {
        // 실시간 정보 업데이트
        _currentContext.CurrentDateTime = DateTime.Now;
        _currentContext.AvailableMemory = GC.GetTotalMemory(false);
        
        try
        {
            _currentContext.UserProfile = await _userProfile.GetCurrentProfileAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not load user profile");
        }
        
        return _currentContext;
    }
    
    public async Task UpdateLocationAsync(LocationInfo location, CancellationToken cancellationToken = default)
    {
        _currentContext.Location = location;
        _currentContext.Country = location.Country;
        await Task.CompletedTask;
    }
    
    public async Task UpdateSystemMetricsAsync(SystemMetrics metrics, CancellationToken cancellationToken = default)
    {
        _currentContext.CpuUsage = metrics.CpuUsagePercentage;
        _currentContext.AvailableMemory = metrics.AvailableMemoryMB;
        await Task.CompletedTask;
    }
    
    public async Task<string> FormatContextForPromptAsync(ContextLevel level = ContextLevel.Standard)
    {
        var context = await GetCurrentContextAsync();
        
        return level switch
        {
            ContextLevel.Minimal => FormatMinimalContext(context),
            ContextLevel.Standard => FormatStandardContext(context),
            ContextLevel.Detailed => FormatDetailedContext(context),
            ContextLevel.Full => FormatFullContext(context),
            _ => FormatStandardContext(context)
        };
    }
    
    public async Task RefreshDynamicDataAsync()
    {
        try
        {
            // 성능 메트릭 업데이트
            var metrics = await _performanceCounter.GetCurrentMetricsAsync();
            await UpdateSystemMetricsAsync(metrics);
            
            // 위치 정보 업데이트 (가끔씩)
            if (_locationService.IsLocationEnabled && _currentContext.Location == null)
            {
                var location = await _locationService.GetApproximateLocationAsync();
                if (location != null)
                {
                    await UpdateLocationAsync(location);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to refresh dynamic system data");
        }
    }
    
    private string FormatStandardContext(SystemContext context)
    {
        var sb = new StringBuilder();
        
        // 필수 시간 정보
        sb.AppendLine($"**CURRENT TIME**: {context.FormattedDateTime} ({context.TimeZone.StandardName})");
        sb.AppendLine($"**DAY**: {context.DayOfWeek}, {context.Season}");
        sb.AppendLine();
        
        // 위치 정보 (있는 경우)
        if (context.Location != null)
        {
            var locationStr = context.Location.IsApproximate ? "근처" : "위치";
            sb.AppendLine($"**LOCATION**: {context.Location.City}, {context.Location.Country} {locationStr}");
        }
        
        // 언어/문화 정보
        sb.AppendLine($"**LOCALE**: {context.Language}, Currency: {context.Currency}");
        sb.AppendLine();
        
        // 시스템 능력
        sb.AppendLine($"**SYSTEM**: {context.OperatingSystem}");
        sb.AppendLine($"**WORKING DIR**: {context.WorkingDirectory}");
        
        if (context.AvailableTools.Any())
        {
            sb.AppendLine($"**AVAILABLE TOOLS**: {string.Join(", ", context.AvailableTools.Take(5))}");
            if (context.AvailableTools.Count > 5)
                sb.AppendLine($"... and {context.AvailableTools.Count - 5} more tools");
        }
        
        // 사용자 선호사항 (있는 경우)
        if (context.UserProfile?.PreferredName != null)
        {
            sb.AppendLine();
            sb.AppendLine($"**USER**: {context.UserProfile.PreferredName}");
            if (context.UserProfile.Preferences.Any())
            {
                sb.AppendLine($"**PREFERENCES**: {string.Join(", ", context.UserProfile.Preferences.Take(3))}");
            }
        }
        
        sb.AppendLine();
        return sb.ToString();
    }
    
    private string FormatMinimalContext(SystemContext context)
    {
        return $"Current: {context.FormattedDateTime} ({context.TimeZone.StandardName}), " +
               $"OS: {Environment.OSVersion.Platform}, " +
               $"Lang: {context.Language}";
    }
    
    private string FormatDetailedContext(SystemContext context)
    {
        var basic = FormatStandardContext(context);
        var sb = new StringBuilder(basic);
        
        // 성능 정보 추가
        sb.AppendLine($"**PERFORMANCE**: CPU {context.CpuUsage:F1}%, RAM {context.AvailableMemory / (1024*1024)}MB available");
        sb.AppendLine($"**SESSION**: {context.SessionDuration} minutes active");
        
        return sb.ToString();
    }
    
    private string FormatFullContext(SystemContext context)
    {
        var detailed = FormatDetailedContext(context);
        var sb = new StringBuilder(detailed);
        
        // 모든 추가 정보
        sb.AppendLine($"**MACHINE**: {context.MachineName}");
        sb.AppendLine($"**AGENT**: Version {context.AgentVersion}");
        
        if (context.CustomContext.Any())
        {
            sb.AppendLine($"**CUSTOM**: {string.Join(", ", context.CustomContext.Take(3).Select(kv => $"{kv.Key}={kv.Value}"))}");
        }
        
        return sb.ToString();
    }
    
    public void Dispose()
    {
        _refreshTimer?.Dispose();
    }
}