using System.Text.Json;
using McpAgent.Models;
using Microsoft.Extensions.Logging;

namespace McpAgent.Services;

public class UserProfileService : IUserProfileService
{
    private readonly ILogger<UserProfileService> _logger;
    private readonly string _profilePath;
    private UserProfile? _cachedProfile;
    
    public UserProfileService(ILogger<UserProfileService> logger)
    {
        _logger = logger;
        _profilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
            ".mcpagent", "user_profile.json");
    }
    
    public async Task<UserProfile> GetCurrentProfileAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedProfile != null)
        {
            return _cachedProfile;
        }
        
        try
        {
            if (File.Exists(_profilePath))
            {
                var json = await File.ReadAllTextAsync(_profilePath, cancellationToken);
                _cachedProfile = JsonSerializer.Deserialize<UserProfile>(json) ?? new UserProfile();
            }
            else
            {
                _cachedProfile = new UserProfile();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not load user profile, using defaults");
            _cachedProfile = new UserProfile();
        }
        
        return _cachedProfile;
    }
    
    public async Task UpdateProfileAsync(UserProfile profile, CancellationToken cancellationToken = default)
    {
        try
        {
            var directory = Path.GetDirectoryName(_profilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            
            await File.WriteAllTextAsync(_profilePath, json, cancellationToken);
            _cachedProfile = profile;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save user profile");
        }
    }
}