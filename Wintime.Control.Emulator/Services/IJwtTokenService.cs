using Microsoft.Extensions.Options;
using MQTTnet.Internal;
using Wintime.Control.Emulator.Config;
using Wintime.Control.Emulator.Models;

namespace Wintime.Control.Emulator.Services;

public interface IJwtTokenService
{
    Task<string> GetTokenAsync(CancellationToken ct);
}

public class JwtTokenService : IJwtTokenService
{
    private readonly HttpClient _httpClient;
    private readonly AuthSettings _auth;
    private readonly ILogger<JwtTokenService> _logger;
    private readonly AsyncLock _lock = new();
    private string? _cachedAccessToken;
    private string? _cachedRefreshToken;
    private DateTime _tokenExpiry;

    public JwtTokenService(
        HttpClient httpClient, 
        IOptions<EmulatorSettings> settings,
        ILogger<JwtTokenService> logger)
    {
        _httpClient = httpClient;
        _auth = settings.Value.MainApi.Auth;
        _logger = logger;
    }

    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        using (await _lock.EnterAsync(ct))
        {
            if (!string.IsNullOrEmpty(_cachedAccessToken) && _tokenExpiry > DateTime.UtcNow.AddMinutes(1))
                return _cachedAccessToken!;

            if (!string.IsNullOrEmpty(_cachedRefreshToken))
            {
                _logger.LogDebug("Attempting token refresh");
                if (await TryRefreshTokenAsync(ct))
                {
                    _logger.LogDebug("Token refreshed successfully");
                    return _cachedAccessToken!;
                }
                _logger.LogWarning("Token refresh failed, performing full login");
            }

            _logger.LogDebug("Performing full login");
            await LoginAsync(ct);
            return _cachedAccessToken!;
        }
    }

    private async Task<bool> TryRefreshTokenAsync(CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrEmpty(_cachedAccessToken) || string.IsNullOrEmpty(_cachedRefreshToken))
                return false;

            var request = new RefreshRequest
            {
                AccessToken = _cachedAccessToken,
                RefreshToken = _cachedRefreshToken
            };

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_auth.TimeoutSec));

            var response = await _httpClient.PostAsJsonAsync(
                _auth.RefreshEndpoint, request, timeoutCts.Token);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Refresh failed with status {StatusCode}", response.StatusCode);
                return false;
            }
            
            var tokens = await response.Content.ReadFromJsonAsync<LoginResponse>(timeoutCts.Token);
            if (tokens == null)
                return false;
            
            _cachedAccessToken = tokens.AccessToken;
            _cachedRefreshToken = tokens.RefreshToken;
            _tokenExpiry = tokens.ExpiresAt;
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Exception during token refresh");
            return false;
        }
    }


    private async Task LoginAsync(CancellationToken ct)
    {
        var request = new LoginRequest
        {
            Login = _auth.Username,
            Password = _auth.Password
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_auth.TimeoutSec));

        var response = await _httpClient.PostAsJsonAsync(
            _auth.LoginEndpoint, request, timeoutCts.Token);
        
        response.EnsureSuccessStatusCode();
        
        var tokens = await response.Content.ReadFromJsonAsync<LoginResponse>(timeoutCts.Token)
            ?? throw new InvalidOperationException("Failed to parse login response");
        
        _cachedAccessToken = tokens.AccessToken;
        _cachedRefreshToken = tokens.RefreshToken;
        _tokenExpiry = tokens.ExpiresAt;
        
        _logger.LogInformation("Logged in as {Username}, token expires at {ExpiresAt}", 
            _auth.Username, _tokenExpiry);
    }

}