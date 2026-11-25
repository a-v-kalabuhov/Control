using Microsoft.EntityFrameworkCore;
using Npgsql;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Exceptions;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;

namespace Wintime.Control.Infrastructure.Services;

public sealed class ShiftPlanMaterializer : IShiftPlanMaterializer
{
    private const string ConcurrencyMessage = "План смены уже изменён другим пользователем";

    private readonly ControlDbContext _context;
    private readonly IShiftLeadFallbackProvider _fallbackProvider;

    public ShiftPlanMaterializer(
        ControlDbContext context,
        IShiftLeadFallbackProvider fallbackProvider)
    {
        _context = context;
        _fallbackProvider = fallbackProvider;
    }

    public async Task<ShiftPlanDay> MaterializeAsync(
        DateOnly date, int position, CancellationToken ct)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            try
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(ct);
                var initial = await ReadSnapshotAsync(date, position, ct);
                await ShiftScheduleLock.AcquireAsync(_context, ct);

                var dayId = await GetDayIdAsync(date, ct);
                if (dayId != initial.DayId)
                    throw new ShiftConcurrencyException(ConcurrencyMessage);

                var day = await LoadTrackedDayForUpdateAsync(date, ct);
                var item = day.Items.SingleOrDefault(i => i.Position == position)
                    ?? throw new ShiftNotFoundException("Позиция смены не найдена");
                if (dayId != Guid.Empty &&
                    (initial.Item == null || item.Id != initial.Item.Id ||
                     item.ShiftLeadId != initial.Item.ShiftLeadId ||
                     item.Closure?.State != initial.Item.ClosureState))
                    throw new ShiftConcurrencyException(ConcurrencyMessage);

                var fallback = await _fallbackProvider.GetAsync(ct);
                if (EffectiveShiftLeadResolver.Resolve(item, fallback).Id == null)
                    throw new DomainException("Нельзя материализовать смену без старшего");

                if (dayId == Guid.Empty)
                {
                    _context.ShiftPlanDays.Add(day);
                    await _context.SaveChangesAsync(ct);
                }

                await transaction.CommitAsync(ct);
                return day;
            }
            catch (Exception exception) when (IsConcurrencyViolation(exception))
            {
                throw new ShiftConcurrencyException(ConcurrencyMessage);
            }
        });
    }

    public async Task<ShiftPlanDay> LoadTrackedDayForUpdateAsync(DateOnly date, CancellationToken ct)
    {
        var dayId = await GetDayIdAsync(date, ct);
        if (dayId == Guid.Empty)
        {
            var shifts = await _context.Shifts.AsNoTracking().ToListAsync(ct);
            return ShiftPlanCandidateBuilder.BuildVirtualDay(date, shifts);
        }

        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT * FROM \"ShiftPlanDays\" WHERE \"Id\" = {dayId} FOR UPDATE", ct);
        return await _context.ShiftPlanDays
            .Include(d => d.Items).ThenInclude(i => i.ShiftLead)
            .Include(d => d.Items).ThenInclude(i => i.OperatorAssignments)
            .Include(d => d.Items).ThenInclude(i => i.Closure)
            .SingleOrDefaultAsync(d => d.Id == dayId, ct)
            ?? throw new ShiftConcurrencyException(ConcurrencyMessage);
    }

    private async Task<Guid> GetDayIdAsync(DateOnly date, CancellationToken ct) =>
        await _context.ShiftPlanDays
            .Where(d => d.ShiftDate == date)
            .Select(d => d.Id)
            .SingleOrDefaultAsync(ct);

    private async Task<MaterializationSnapshot> ReadSnapshotAsync(
        DateOnly date, int position, CancellationToken ct)
    {
        var day = await _context.ShiftPlanDays
            .AsNoTracking()
            .Where(d => d.ShiftDate == date)
            .Select(d => new
            {
                d.Id,
                Item = d.Items.Where(i => i.Position == position)
                    .Select(i => new ItemSnapshot(
                        i.Id,
                        i.ShiftLeadId,
                        i.Closure == null ? (ShiftClosureState?)null : i.Closure.State))
                    .SingleOrDefault()
            })
            .SingleOrDefaultAsync(ct);
        return new MaterializationSnapshot(day?.Id ?? Guid.Empty, day?.Item);
    }

    private static bool IsConcurrencyViolation(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is PostgresException
                {
                    SqlState: PostgresErrorCodes.UniqueViolation or
                        PostgresErrorCodes.SerializationFailure or
                        PostgresErrorCodes.DeadlockDetected
                })
                return true;
        }
        return false;
    }

    private sealed record MaterializationSnapshot(Guid DayId, ItemSnapshot? Item);
    private sealed record ItemSnapshot(
        Guid Id, string? ShiftLeadId, ShiftClosureState? ClosureState);
}
