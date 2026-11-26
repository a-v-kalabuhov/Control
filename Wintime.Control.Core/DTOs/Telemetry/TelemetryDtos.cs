namespace Wintime.Control.Core.DTOs.Telemetry;

public sealed record PeriodCountDto(DateOnly StartDate, DateOnly EndDate, long Count, bool IsCurrent);

public sealed record TelemetryStatisticsDto(
    long TotalCount,
    long TableSizeBytes,
    long IndexesSizeBytes,
    PeriodCountDto CurrentMonth,
    IReadOnlyList<PeriodCountDto> RecentWeeks,
    IReadOnlyList<PeriodCountDto> PreviousMonths);
