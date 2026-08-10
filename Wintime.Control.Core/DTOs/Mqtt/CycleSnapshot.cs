namespace Wintime.Control.Core.DTOs.Mqtt;

public sealed record CycleSnapshot(int Number, DateTime StartTime, DateTime? InjectionStartTime, decimal? Cushion);
