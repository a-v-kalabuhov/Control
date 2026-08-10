namespace Wintime.Control.Core.DTOs.Mqtt;

public sealed record CompletedCycleSnapshot(
    int Number, DateTime StartTime, DateTime EndTime, DateTime? InjectionStartTime, decimal? Cushion);
