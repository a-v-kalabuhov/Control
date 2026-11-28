using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Wintime.Control.Core.DTOs.Telemetry;
using Wintime.Control.Core.Exceptions;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;

namespace Wintime.Control.Infrastructure.Services;

public sealed class TelemetryAdministrationService : ITelemetryAdministrationService
{
    private const int TelemetryAdminCommandTimeoutSeconds = 30 * 60;
    private readonly ControlDbContext _context;
    private readonly ILogger<TelemetryAdministrationService> _logger;

    public TelemetryAdministrationService(
        ControlDbContext context, ILogger<TelemetryAdministrationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<TelemetryStatisticsDto> GetStatisticsAsync(string? timeZone, CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        var periods = TelemetryPeriodCalculator.CreateStatisticsPeriods(nowUtc, timeZone);
        var previousTimeout = _context.Database.GetCommandTimeout();
        try
        {
            _context.Database.SetCommandTimeout(TelemetryAdminCommandTimeoutSeconds);
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
        finally
        {
            _context.Database.SetCommandTimeout(previousTimeout);
        }
    }

    public async Task<TelemetryCleanupPreviewDto> PreviewCleanupAsync(
        DateOnly date, string? timeZone, CancellationToken ct)
    {
        var cutoffUtc = TelemetryPeriodCalculator.GetCleanupCutoffUtc(date, DateTime.UtcNow, timeZone);
        var previousTimeout = _context.Database.GetCommandTimeout();
        try
        {
            _context.Database.SetCommandTimeout(TelemetryAdminCommandTimeoutSeconds);
            var count = await _context.Telemetry.LongCountAsync(t => t.Timestamp < cutoffUtc, ct);
            return new TelemetryCleanupPreviewDto(date, cutoffUtc, count);
        }
        finally
        {
            _context.Database.SetCommandTimeout(previousTimeout);
        }
    }

    public async Task<TelemetryCleanupResultDto> CleanupAsync(
        DateOnly date, string? timeZone, string adminUserId, CancellationToken ct)
    {
        var cutoffUtc = TelemetryPeriodCalculator.GetCleanupCutoffUtc(date, DateTime.UtcNow, timeZone);
        var previousTimeout = _context.Database.GetCommandTimeout();
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "Начало очистки Telemetry администратором {AdminUserId}, cutoffUtc={CutoffUtc:O}",
            adminUserId, cutoffUtc);
        try
        {
            _context.Database.SetCommandTimeout(TelemetryAdminCommandTimeoutSeconds);
            var deletedCount = await _context.Telemetry
                .Where(t => t.Timestamp < cutoffUtc)
                .ExecuteDeleteAsync(ct);
            _logger.LogInformation(
                "Очистка Telemetry завершена администратором {AdminUserId}, cutoffUtc={CutoffUtc:O}, deletedCount={DeletedCount}, duration={Duration}",
                adminUserId, cutoffUtc, deletedCount, stopwatch.Elapsed);
            return new TelemetryCleanupResultDto(cutoffUtc, deletedCount);
        }
        catch (Exception ex) when (IsCommandTimeout(ex, ct))
        {
            _logger.LogWarning(ex,
                "Таймаут очистки Telemetry администратором {AdminUserId}, cutoffUtc={CutoffUtc:O}, duration={Duration}",
                adminUserId, cutoffUtc, stopwatch.Elapsed);
            throw new TelemetryCleanupTimeoutException("Очистка телеметрии превысила 30 минут");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Ошибка очистки Telemetry администратором {AdminUserId}, cutoffUtc={CutoffUtc:O}, duration={Duration}",
                adminUserId, cutoffUtc, stopwatch.Elapsed);
            throw;
        }
        finally
        {
            _context.Database.SetCommandTimeout(previousTimeout);
        }
    }

    private static bool IsCommandTimeout(Exception exception, CancellationToken ct) =>
        !ct.IsCancellationRequested &&
        (exception is TimeoutException or OperationCanceledException ||
         exception is NpgsqlException { InnerException: TimeoutException });

    private static PeriodCountDto ToDto(TelemetryPeriod period, long count) =>
        new(period.StartDate, period.EndDate, count, period.IsCurrent);
}

public sealed record TelemetryStatisticsRow(
    long TotalCount, long CurrentMonthCount,
    long Week0Count, long Week1Count, long Week2Count, long Week3Count,
    long Month0Count, long Month1Count, long Month2Count);

public sealed record TelemetrySizeRow(long TableSizeBytes, long IndexesSizeBytes);
