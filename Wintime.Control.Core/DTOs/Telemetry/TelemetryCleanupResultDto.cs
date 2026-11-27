namespace Wintime.Control.Core.DTOs.Telemetry;

public sealed record TelemetryCleanupResultDto(DateTime CutoffUtc, long DeletedCount);
