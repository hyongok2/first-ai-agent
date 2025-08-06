using System.Globalization;

namespace McpAgent.Models;

public class SystemContext
{
    // 시간 정보
    public DateTime CurrentDateTime { get; set; } = DateTime.Now;
    public TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.Local;
    public string FormattedDateTime => CurrentDateTime.ToString("yyyy-MM-dd HH:mm:ss K");
    public DayOfWeek DayOfWeek => CurrentDateTime.DayOfWeek;
    public string Season => GetSeason(CurrentDateTime);
    
    // 위치 정보 (사용자 동의 하에)
    public LocationInfo? Location { get; set; }
    public string? Country { get; set; }
    public string? Language { get; set; } = CultureInfo.CurrentCulture.Name;
    public string? Currency { get; set; } = RegionInfo.CurrentRegion?.ISOCurrencySymbol;
    
    // 시스템 정보
    public string OperatingSystem { get; set; } = Environment.OSVersion.ToString();
    public string MachineName { get; set; } = Environment.MachineName;
    public long AvailableMemory { get; set; }
    public double CpuUsage { get; set; }
    
    // 애플리케이션 컨텍스트
    public string AgentVersion { get; set; } = "2.0.0";
    public int SessionDuration { get; set; } // 분 단위
    public string WorkingDirectory { get; set; } = Environment.CurrentDirectory;
    public List<string> AvailableTools { get; set; } = new();
    
    // 사용자 컨텍스트
    public UserProfile? UserProfile { get; set; }
    public Dictionary<string, object> CustomContext { get; set; } = new();

    private static string GetSeason(DateTime date)
    {
        return (date.Month) switch
        {
            12 or 1 or 2 => "Winter",
            3 or 4 or 5 => "Spring", 
            6 or 7 or 8 => "Summer",
            9 or 10 or 11 => "Fall",
            _ => "Unknown"
        };
    }
}

public class LocationInfo
{
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? Country { get; set; }
    public string? PostalCode { get; set; }
    public bool IsApproximate { get; set; } = true; // 정확한 위치가 아닌 대략적 위치
}

public class UserProfile
{
    public string? PreferredName { get; set; }
    public string? TimeFormat { get; set; } = "24h"; // 12h or 24h
    public string? DateFormat { get; set; } = "yyyy-MM-dd";
    public string? PreferredLanguage { get; set; }
    public List<string> Preferences { get; set; } = new();
    public Dictionary<string, int> ToolUsageCount { get; set; } = new();
}

public class SystemMetrics
{
    public double CpuUsagePercentage { get; set; }
    public long AvailableMemoryMB { get; set; }
    public long TotalMemoryMB { get; set; }
    public double DiskUsagePercentage { get; set; }
    public int RunningProcessCount { get; set; }
    public TimeSpan SystemUptime { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.Now;
}

public enum ContextLevel
{
    Minimal,    // 핵심 정보만 (시간, OS, 언어)
    Standard,   // 일반적인 컨텍스트 
    Detailed,   // 상세 정보 포함 (성능, 상세 위치 등)
    Full        // 모든 가용 정보
}