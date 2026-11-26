using Wintime.Control.Core.DTOs.Telemetry;

namespace Wintime.Control.Core.Interfaces;

public interface ITelemetryAdministrationService
{
    Task<TelemetryStatisticsDto> GetStatisticsAsync(string? timeZone, CancellationToken ct);

    Task<TelemetryCleanupPreviewDto> PreviewCleanupAsync(
        DateOnly date, string? timeZone, CancellationToken ct);
}
