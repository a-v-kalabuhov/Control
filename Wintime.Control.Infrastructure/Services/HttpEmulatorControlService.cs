using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Wintime.Control.Core.Interfaces;

namespace Wintime.Control.Infrastructure.Services;

public class HttpEmulatorControlService : IEmulatorControlService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpEmulatorControlService> _logger;

    public HttpEmulatorControlService(HttpClient httpClient, ILogger<HttpEmulatorControlService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task SetModeAsync(string immId, string mode, CancellationToken ct = default)
    {
        try
        {
            var body = new StringContent(
                JsonSerializer.Serialize(new { mode }),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PatchAsync($"api/emulator/instances/{immId}/mode", body, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set emulator mode for IMM {ImmId} to {Mode}", immId, mode);
        }
    }
}
