using System.Text.Json;
using McpAgent.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace McpAgent.Services;

public class PrivacyAwareLocationService : ILocationService
{
    private readonly ILogger<PrivacyAwareLocationService> _logger;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private LocationInfo? _cachedLocation;
    private DateTime _lastLocationUpdate = DateTime.MinValue;
    
    public bool IsLocationEnabled => 
        _configuration.GetValue<bool>("Privacy:LocationServices:Enabled", false);
    
    public PrivacyAwareLocationService(
        ILogger<PrivacyAwareLocationService> logger,
        IConfiguration configuration,
        HttpClient httpClient)
    {
        _logger = logger;
        _configuration = configuration;
        _httpClient = httpClient;
    }
    
    public async Task<LocationInfo?> GetApproximateLocationAsync()
    {
        if (!IsLocationEnabled)
        {
            return GetStaticLocationFromConfig();
        }
        
        // 캐시된 위치가 1시간 이내면 재사용
        if (_cachedLocation != null && 
            DateTime.Now - _lastLocationUpdate < TimeSpan.FromHours(1))
        {
            return _cachedLocation;
        }
        
        try
        {
            // IP 기반 대략적 위치 (정확도 낮음, 개인정보 안전)
            var location = await GetLocationFromIPAsync();
            if (location != null)
            {
                location.IsApproximate = true;
                _cachedLocation = location;
                _lastLocationUpdate = DateTime.Now;
            }
            return location;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not determine approximate location");
            return GetStaticLocationFromConfig();
        }
    }
    
    public async Task<bool> RequestLocationPermissionAsync()
    {
        // 실제 구현에서는 사용자에게 권한 요청
        await Task.CompletedTask;
        return IsLocationEnabled;
    }
    
    private LocationInfo? GetStaticLocationFromConfig()
    {
        // 사용자가 설정한 기본 위치 (도시 수준)
        var city = _configuration["Privacy:DefaultLocation:City"];
        var country = _configuration["Privacy:DefaultLocation:Country"];
        
        if (!string.IsNullOrEmpty(city) && !string.IsNullOrEmpty(country))
        {
            return new LocationInfo
            {
                City = city,
                Country = country,
                IsApproximate = true
            };
        }
        
        return null;
    }
    
    private async Task<LocationInfo?> GetLocationFromIPAsync()
    {
        try
        {
            _httpClient.Timeout = TimeSpan.FromSeconds(5);
            
            // 간단한 공개 IP 위치 서비스 사용 (실제로는 더 신뢰할 수 있는 서비스 사용)
            var response = await _httpClient.GetStringAsync("http://ip-api.com/json/?fields=city,country,region");
            var locationData = JsonSerializer.Deserialize<JsonElement>(response);
            
            if (locationData.TryGetProperty("city", out var city) &&
                locationData.TryGetProperty("country", out var country))
            {
                return new LocationInfo
                {
                    City = city.GetString(),
                    Country = country.GetString(),
                    Region = locationData.TryGetProperty("region", out var region) ? region.GetString() : null,
                    IsApproximate = true
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "IP-based location service failed");
        }
        
        return null;
    }
}