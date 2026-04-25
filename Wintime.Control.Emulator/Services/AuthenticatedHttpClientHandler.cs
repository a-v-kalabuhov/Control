using Microsoft.Extensions.Options;
using Wintime.Control.Emulator.Config;

namespace Wintime.Control.Emulator.Services;

// Custom Authorization Handler for Refit
public class AuthenticatedHttpClientHandler2 : DelegatingHandler
{
    private readonly IJwtTokenService _tokenService;
    public AuthenticatedHttpClientHandler2(IJwtTokenService tokenService) => _tokenService = tokenService;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = await _tokenService.GetTokenAsync(ct);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, ct);
    }
}

public class AuthenticatedHttpClientHandler : DelegatingHandler
{
    private readonly IJwtTokenService? _tokenService;
    private readonly string? _apiKey;
    private readonly string? _apiKeyHeaderName;

    public AuthenticatedHttpClientHandler(
        IOptions<EmulatorSettings> settings,
        IJwtTokenService? tokenService = null)
    {
        var auth = settings.Value.MainApi.Auth;
        
        if (auth.Type == "Login")
        {
            _tokenService = tokenService ?? 
                throw new ArgumentException("JwtTokenService required for Login auth");
        }
        else if (auth.Type == "ApiKey")
        {
            _apiKey = auth.ApiKey;
            _apiKeyHeaderName = auth.ApiKeyHeaderName;
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, 
        CancellationToken ct)
    {
        // Добавляем авторизацию в зависимости от типа
        if (!string.IsNullOrEmpty(_apiKey))
        {
            // API Key режим
            request.Headers.Add(_apiKeyHeaderName!, _apiKey);
        }
        else if (_tokenService != null)
        {
            // JWT Login режим
            var token = await _tokenService.GetTokenAsync(ct);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }
        
        return await base.SendAsync(request, ct);
    }
}