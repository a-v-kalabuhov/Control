using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.Telemetry;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;

namespace Wintime.Control.Infrastructure.Services;

public sealed class TelemetryAdministrationService : ITelemetryAdministrationService
{
    private readonly ControlDbContext _context;

    public TelemetryAdministrationService(ControlDbContext context) => _context = context;

    public async Task<TelemetryStatisticsDto> GetStatisticsAsync(string? timeZone, CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        var periods = TelemetryPeriodCalculator.CreateStatisticsPeriods(nowUtc, timeZone);
        var row = await _context.Database.SqlQuery<TelemetryStatisticsRow>($"""
            SELECT
              COUNT(*) AS "TotalCount",
              COUNT(*) FILTER (WHERE "Timestamp" >= {periods.CurrentMonth.StartUtc} AND "Timestamp" < {periods.CurrentMonth.EndUtc}) AS "CurrentMonthCount",
              COUNT(*) FILTER (WHERE "Timestamp" >= {periods.RecentWeeks[0].StartUtc} AND "Timestamp" < {periods.RecentWeeks[0].EndUtc}) AS "Week0Count",
              COUNT(*) FILTER (WHERE "Timestamp" >= {periods.RecentWeeks[1].StartUtc} AND "Timestamp" < {periods.RecentWeeks[1].EndUtc}) AS "Week1Count",
              COUNT(*) FILTER (WHERE "Timestamp" >= {periods.RecentWeeks[2].StartUtc} AND "Timestamp" < {periods.RecentWeeks[2].EndUtc}) AS "Week2Count",
              COUNT(*) FILTER (WHERE "Timestamp" >= {periods.RecentWeeks[3].StartUtc} AND "Timestamp" < {periods.RecentWeeks[3].EndUtc}) AS "Week3Count",
              COUNT(*) FILTER (WHERE "Timestamp" >= {periods.PreviousMonths[0].StartUtc} AND "Timestamp" < {periods.PreviousMonths[0].EndUtc}) AS "Month0Count",
              COUNT(*) FILTER (WHERE "Timestamp" >= {periods.PreviousMonths[1].StartUtc} AND "Timestamp" < {periods.PreviousMonths[1].EndUtc}) AS "Month1Count",
              COUNT(*) FILTER (WHERE "Timestamp" >= {periods.PreviousMonths[2].StartUtc} AND "Timestamp" < {periods.PreviousMonths[2].EndUtc}) AS "Month2Count"
            FROM "Telemetry"
            """).SingleAsync(ct);
        var sizes = await _context.Database.SqlQuery<TelemetrySizeRow>($"""
            SELECT
              pg_table_size('"Telemetry"')::bigint AS "TableSizeBytes",
              pg_indexes_size('"Telemetry"')::bigint AS "IndexesSizeBytes"
            """).SingleAsync(ct);

        return new TelemetryStatisticsDto(
            row.TotalCount, sizes.TableSizeBytes, sizes.IndexesSizeBytes,
            ToDto(periods.CurrentMonth, row.CurrentMonthCount),
            periods.RecentWeeks.Select((period, index) => ToDto(period, index switch
            {
                0 => row.Week0Count, 1 => row.Week1Count, 2 => row.Week2Count, _ => row.Week3Count
            })).ToArray(),
            periods.PreviousMonths.Select((period, index) => ToDto(period, index switch
            {
                0 => row.Month0Count, 1 => row.Month1Count, _ => row.Month2Count
            })).ToArray());
    }

    private static PeriodCountDto ToDto(TelemetryPeriod period, long count) =>
        new(period.StartDate, period.EndDate, count, period.IsCurrent);
}

public sealed record TelemetryStatisticsRow(
    long TotalCount, long CurrentMonthCount,
    long Week0Count, long Week1Count, long Week2Count, long Week3Count,
    long Month0Count, long Month1Count, long Month2Count);

public sealed record TelemetrySizeRow(long TableSizeBytes, long IndexesSizeBytes);
