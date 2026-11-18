using Microsoft.EntityFrameworkCore;
using Npgsql;
using Wintime.Control.Core.DTOs.ShiftClosures;
using Wintime.Control.Core.DTOs.ShiftStaffing;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Exceptions;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;

namespace Wintime.Control.Infrastructure.Services;

public sealed class ShiftClosureService : IShiftClosureService
{
    private const string ConcurrencyMessage = "Черновик сменного рапорта уже изменён другим пользователем";
    private readonly ControlDbContext _context;
    private readonly IFactoryClock _factoryClock;
    private readonly ShiftClosureReadModelLoader _loader;

    public ShiftClosureService(
        ControlDbContext context, IFactoryClock factoryClock, ShiftClosureReadModelLoader loader)
    {
        _context = context;
        _factoryClock = factoryClock;
        _loader = loader;
    }

    public Task<ShiftClosureDto> GetAsync(
        Guid itemId, StaffingCaller caller, CancellationToken ct) =>
        _loader.LoadAsync(itemId, caller, Now, ct);

    public async Task<ShiftClosureDto> SaveDraftAsync(
        Guid itemId, SaveShiftClosureDraftRequest request, StaffingCaller caller, CancellationToken ct)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            try
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(ct);
                var initial = await _context.ShiftPlanItems
                    .AsNoTracking()
                    .Include(i => i.Closure)
                    .SingleOrDefaultAsync(i => i.Id == itemId, ct)
                    ?? throw new ShiftNotFoundException("Смена не найдена");
                var initialClosureState = initial.Closure?.State;

                // Общий порядок со staffing: schedule advisory -> ShiftPlanItem.
                // Начальное состояние позволяет отличить параллельное создание от повторного PUT.
                await ShiftScheduleLock.AcquireAsync(_context, ct);
                var item = await _context.ShiftPlanItems
                    .FromSqlInterpolated(
                        $"SELECT * FROM \"ShiftPlanItems\" WHERE \"Id\" = {itemId} FOR UPDATE")
                    .Include(i => i.Closure).ThenInclude(c => c!.Entries)
                    .SingleOrDefaultAsync(ct)
                    ?? throw new ShiftConcurrencyException(ConcurrencyMessage);
                EnsureAccess(item, caller);
                if (item.Closure?.State != initialClosureState)
                    throw new ShiftConcurrencyException(ConcurrencyMessage);
                if (item.Closure?.State == ShiftClosureState.Closed)
                    throw new DomainException("shiftClosed");

                // Состав и FK берутся только из актуальной серверной read model.
                var readModel = await _loader.LoadAsync(itemId, caller, Now, ct);
                var currentByTask = readModel.Entries.ToDictionary(e => e.TaskId);
                var errors = new List<ShiftClosureFieldError>();
                foreach (var input in request.Entries)
                {
                    if (!currentByTask.TryGetValue(input.ShiftTaskId, out var current))
                        throw new ShiftNotFoundException("Задание не принадлежит этой смене");
                    errors.AddRange(ShiftClosureValidator.Validate(new ShiftClosureValidationEntry
                    {
                        TaskId = input.ShiftTaskId,
                        HasOperators = current.HasOperators,
                        IsManualFallbackRequired = current.IsManualFallbackRequired,
                        AutomaticOutputQuantity = current.AutomaticOutputQuantity,
                        TaskPackagingUnit = current.TaskPackagingUnit,
                        ActualPartWeightGrams = input.ActualPartWeightGrams,
                        ManualFullCycleSeconds = input.ManualFullCycleSeconds,
                        ActualPackagingUnit = input.ActualPackagingUnit,
                        PackagingPlaces = input.PackagingPlaces,
                        ManualGoodQuantity = input.ManualGoodQuantity,
                        ManualDefectQuantity = input.ManualDefectQuantity
                    }, requireCompleteness: false));
                }
                if (errors.Count > 0)
                {
                    throw new ShiftClosureValidationException(
                        "draftValidationFailed", "Проверьте ручные поля черновика", errors);
                }

                var closure = item.Closure;
                if (closure == null)
                {
                    closure = new ShiftClosure
                    {
                        ShiftPlanItemId = item.Id,
                        State = ShiftClosureState.Draft,
                        CreatedById = caller.UserId,
                        CreatedAt = Now
                    };
                    _context.ShiftClosures.Add(closure);
                }

                foreach (var input in request.Entries)
                {
                    var entry = closure.Entries.SingleOrDefault(e => e.ShiftTaskId == input.ShiftTaskId);
                    if (entry == null)
                    {
                        entry = new ShiftClosureEntry
                        {
                            ShiftClosureId = closure.Id,
                            ShiftTaskId = input.ShiftTaskId,
                            ImmId = currentByTask[input.ShiftTaskId].ImmId
                        };
                        closure.Entries.Add(entry);
                    }
                    entry.ActualPartWeightGrams = input.ActualPartWeightGrams;
                    entry.ManualFullCycleSeconds = input.ManualFullCycleSeconds;
                    entry.ActualPackagingUnit = input.ActualPackagingUnit;
                    entry.PackagingPlaces = input.PackagingPlaces;
                    entry.ManualGoodQuantity = input.ManualGoodQuantity;
                    entry.ManualDefectQuantity = input.ManualDefectQuantity;
                }

                await _context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return await GetAsync(itemId, caller, ct);
            }
            catch (Exception exception) when (IsConcurrencyFailure(exception))
            {
                throw new ShiftConcurrencyException(ConcurrencyMessage);
            }
        });
    }

    public async Task<ShiftClosureDto> CloseAsync(
        Guid itemId, CloseShiftClosureRequest request, StaffingCaller caller, CancellationToken ct)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            try
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(ct);

                // Общий порядок со staffing: schedule advisory -> ShiftPlanItem FOR UPDATE.
                // Все проверки и весь расчёт выполняются повторно после блокировок.
                await ShiftScheduleLock.AcquireAsync(_context, ct);
                var item = await _context.ShiftPlanItems
                    .FromSqlInterpolated(
                        $"SELECT * FROM \"ShiftPlanItems\" WHERE \"Id\" = {itemId} FOR UPDATE")
                    .Include(i => i.Closure).ThenInclude(c => c!.Entries).ThenInclude(e => e.OutputShares)
                    .SingleOrDefaultAsync(ct)
                    ?? throw new ShiftNotFoundException("Смена не найдена");
                EnsureAccess(item, caller);
                if (item.Closure?.State == ShiftClosureState.Closed)
                    throw new ShiftConcurrencyException(ConcurrencyMessage);

                var now = Now;
                ShiftClosureValidator.ValidateCloseTiming(
                    now, item.StartAtUtc, item.EndAtUtc, request.ConfirmEarlyClose);

                // Loader повторно читает задания, циклы, назначения и сырые статусы
                // в этом же DbContext и после lock; pre-lock DTO здесь не существует.
                var readModel = await _loader.LoadAsync(itemId, caller, now, ct);
                var errors = new List<ShiftClosureFieldError>();
                foreach (var current in readModel.Entries)
                {
                    errors.AddRange(ShiftClosureValidator.Validate(new ShiftClosureValidationEntry
                    {
                        TaskId = current.TaskId,
                        HasOperators = current.HasOperators,
                        IsManualFallbackRequired = current.IsManualFallbackRequired,
                        AutomaticOutputQuantity = current.AutomaticOutputQuantity,
                        TaskPackagingUnit = current.TaskPackagingUnit,
                        ActualPartWeightGrams = current.ActualPartWeightGrams,
                        ManualFullCycleSeconds = current.ManualFullCycleSeconds,
                        ActualPackagingUnit = current.ActualPackagingUnit,
                        PackagingPlaces = current.PackagingPlaces,
                        ManualGoodQuantity = current.HasOperators ? current.ManualGoodQuantity : 0,
                        ManualDefectQuantity = current.HasOperators ? current.ManualDefectQuantity : 0
                    }, requireCompleteness: true));
                }
                if (errors.Count > 0)
                {
                    throw new ShiftClosureValidationException(
                        "closeValidationFailed", "Проверьте поля закрытия сменного рапорта", errors);
                }
                if (readModel.Timelines.SelectMany(t => t.Intervals).Any(i => i.HasOrphanOutput))
                    throw new DomainException("orphanOutput");

                var closure = item.Closure;
                if (closure == null)
                {
                    closure = new ShiftClosure
                    {
                        ShiftPlanItemId = item.Id,
                        State = ShiftClosureState.Draft,
                        CreatedById = caller.UserId,
                        CreatedAt = now
                    };
                    _context.ShiftClosures.Add(closure);
                    item.Closure = closure;
                }

                var currentTaskIds = readModel.Entries.Select(e => e.TaskId).ToHashSet();
                foreach (var stale in closure.Entries.Where(e => !currentTaskIds.Contains(e.ShiftTaskId)).ToList())
                {
                    _context.ShiftClosureEntries.Remove(stale);
                    closure.Entries.Remove(stale);
                }

                foreach (var current in readModel.Entries)
                {
                    var entry = closure.Entries.SingleOrDefault(e => e.ShiftTaskId == current.TaskId);
                    if (entry == null)
                    {
                        entry = new ShiftClosureEntry
                        {
                            ShiftClosureId = closure.Id,
                            ShiftTaskId = current.TaskId,
                            ImmId = current.ImmId
                        };
                        closure.Entries.Add(entry);
                    }

                    entry.ImmId = current.ImmId;
                    entry.ImmNameSnapshot = current.ImmName;
                    entry.ProductNameSnapshot = current.ProductName;
                    entry.PlasticIngredientsSnapshot = current.PlasticIngredients;
                    entry.ColorantIngredientsSnapshot = current.ColorantIngredients;
                    entry.RemainingAtStart = current.RemainingAtStart;
                    entry.NormPartWeightGrams = current.NormPartWeightGrams;
                    entry.PlannedFullCycleSeconds = current.PlannedFullCycleSeconds;
                    entry.TaskPackagingUnit = current.TaskPackagingUnit;
                    entry.Cavities = current.Cavities;
                    entry.ShiftPlanQuantity = current.ShiftPlanQuantity;
                    entry.PlannedMaterialWeightGrams = current.PlannedMaterialWeightGrams;
                    entry.AutomaticOutputQuantity = current.AutomaticOutputQuantity;
                    entry.AutomaticAverageFullCycleSeconds = current.AutomaticAverageFullCycleSeconds;
                    entry.IsManualFallbackRequired = current.IsManualFallbackRequired;
                    entry.ActualPartWeightGrams = current.ActualPartWeightGrams;
                    entry.ManualFullCycleSeconds = current.ManualFullCycleSeconds;
                    entry.ActualPackagingUnit = current.ActualPackagingUnit;
                    entry.PackagingPlaces = current.PackagingPlaces;
                    entry.ManualGoodQuantity = current.HasOperators ? current.ManualGoodQuantity : 0;
                    entry.ManualDefectQuantity = current.HasOperators ? current.ManualDefectQuantity : 0;

                    if (entry.OutputShares.Count > 0)
                    {
                        _context.ShiftClosureOutputShares.RemoveRange(entry.OutputShares);
                        entry.OutputShares.Clear();
                    }
                    foreach (var share in current.OutputShares)
                    {
                        entry.OutputShares.Add(new ShiftClosureOutputShare
                        {
                            ShiftClosureEntryId = entry.Id,
                            IntervalStartUtc = DateTime.SpecifyKind(share.IntervalStartUtc, DateTimeKind.Utc),
                            IntervalEndUtc = DateTime.SpecifyKind(share.IntervalEndUtc, DateTimeKind.Utc),
                            OperatorId = share.OperatorId,
                            OperatorFullNameSnapshot = share.OperatorFullName,
                            OperatorEmployeeIdSnapshot = share.OperatorEmployeeId,
                            SlotAtIntervalStart = share.SlotAtIntervalStart,
                            Quantity = share.Quantity
                        });
                    }
                }

                closure.State = ShiftClosureState.Closed;
                closure.ClosedAt = now;
                closure.ClosedById = caller.UserId;
                await _context.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return await GetAsync(itemId, caller, ct);
            }
            catch (Exception exception) when (IsConcurrencyFailure(exception))
            {
                throw new ShiftConcurrencyException(ConcurrencyMessage);
            }
        });
    }

    public async Task<IReadOnlyList<ShiftClosurePendingDto>> GetPendingAsync(
        StaffingCaller caller, CancellationToken ct)
    {
        var now = Now;
        var query = _context.ShiftPlanItems
            .AsNoTracking()
            .Include(i => i.Day)
            .Include(i => i.ShiftLead)
            .Include(i => i.Closure)
            .Where(i => i.ShiftLeadId != null && i.EndAtUtc <= now
                && (i.Closure == null || i.Closure.State != ShiftClosureState.Closed));
        if (caller.IsShiftLead)
            query = query.Where(i => i.ShiftLeadId == caller.UserId);
        var items = await query.OrderByDescending(i => i.StartAtUtc).ThenByDescending(i => i.Id)
            .ToListAsync(ct);
        var orphanByItem = await _loader.GetOrphanOutputByItemAsync(items, now, ct);
        return items.Select(item => new ShiftClosurePendingDto
        {
            ShiftPlanItemId = item.Id,
            ShiftDate = item.Day.ShiftDate,
            Position = item.Position,
            StartAtUtc = item.StartAtUtc,
            EndAtUtc = item.EndAtUtc,
            TimeZoneId = item.TimeZoneId,
            ShiftLeadId = item.ShiftLeadId!,
            ShiftLeadName = item.ShiftLead?.FullName ?? string.Empty,
            HasOrphanOutput = orphanByItem[item.Id]
        }).ToList();
    }

    public Task<ShiftClosureDto> GetClosedAsync(
        Guid closureId, StaffingCaller caller, CancellationToken ct) =>
        _loader.LoadClosedAsync(closureId, caller, ct);

    private static void EnsureAccess(ShiftPlanItem item, StaffingCaller caller)
    {
        if (string.IsNullOrEmpty(item.ShiftLeadId))
            throw new DomainException("shiftLeadRequired");
        if (caller.IsShiftLead && item.ShiftLeadId != caller.UserId)
            throw new ShiftAccessException("Старшему доступны только собственные смены");
        if (!caller.IsShiftLead && !caller.IsManagerOrAdmin)
            throw new ShiftAccessException("Недостаточно прав для изменения сменного рапорта");
    }

    private static bool IsConcurrencyFailure(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is PostgresException postgres &&
                (postgres.SqlState == PostgresErrorCodes.UniqueViolation ||
                 postgres.SqlState == PostgresErrorCodes.ExclusionViolation ||
                 postgres.SqlState == PostgresErrorCodes.SerializationFailure ||
                 postgres.SqlState == PostgresErrorCodes.DeadlockDetected))
                return true;
        }
        return false;
    }

    private DateTime Now => DateTime.SpecifyKind(_factoryClock.UtcNow, DateTimeKind.Utc);
}
