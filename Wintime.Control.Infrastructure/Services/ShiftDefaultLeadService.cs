using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.Shifts;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Exceptions;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;

namespace Wintime.Control.Infrastructure.Services;

public sealed class ShiftDefaultLeadService : IShiftDefaultLeadService
{
    private const string StalePositionsMessage =
        "График смен уже изменён; перезагрузите настройки старших по умолчанию";

    private readonly ControlDbContext _context;
    private readonly IShiftLeadFallbackProvider _fallbackProvider;

    public ShiftDefaultLeadService(
        ControlDbContext context, IShiftLeadFallbackProvider fallbackProvider)
    {
        _context = context;
        _fallbackProvider = fallbackProvider;
    }

    public async Task<ShiftDefaultLeadSettingsDto> GetAsync(CancellationToken ct)
    {
        var shifts = await _context.Shifts.AsNoTracking().OrderBy(s => s.StartMinutes).ToListAsync(ct);
        var snapshot = await _fallbackProvider.GetAsync(ct);
        return BuildResponse(shifts, snapshot.DefaultLeadIdsByPosition, snapshot);
    }

    public async Task<ShiftDefaultLeadSettingsDto> SaveAsync(
        SaveShiftDefaultLeadsRequestDto request, CancellationToken ct)
    {
        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(ct);
            await ShiftScheduleLock.AcquireAsync(_context, ct);

            var shifts = await _context.Shifts.AsNoTracking().OrderBy(s => s.StartMinutes).ToListAsync(ct);
            var requested = ValidatePositions(request, shifts.Count);
            var existing = await _context.ShiftDefaultLeads.ToListAsync(ct);
            var snapshot = await _fallbackProvider.GetAsync(ct);

            ValidateCandidates(requested.Values, snapshot);
            var requestedDefaults = requested
                .Where(p => p.Value != null)
                .ToDictionary(p => p.Key, p => p.Value!);
            var clearedPositions = existing
                .Where(d => !requestedDefaults.ContainsKey(d.Position))
                .Select(d => d.Position)
                .ToList();
            await EnsureNoOperationalDataLeftWithoutLeadAsync(clearedPositions, snapshot, ct);

            foreach (var defaultLead in existing)
            {
                if (!requested.TryGetValue(defaultLead.Position, out var shiftLeadId) || shiftLeadId == null)
                    _context.ShiftDefaultLeads.Remove(defaultLead);
                else if (defaultLead.ShiftLeadId != shiftLeadId)
                    defaultLead.ShiftLeadId = shiftLeadId;
            }

            var existingPositions = existing.Select(d => d.Position).ToHashSet();
            foreach (var (position, shiftLeadId) in requestedDefaults)
            {
                if (!existingPositions.Contains(position))
                    _context.ShiftDefaultLeads.Add(new ShiftDefaultLead
                    {
                        Position = position,
                        ShiftLeadId = shiftLeadId
                    });
            }

            await CleanupEmptyDaysWithoutEffectiveLeadAsync(
                requestedDefaults, snapshot.FirstActiveManagerId, ct);
            await _context.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return BuildResponse(shifts, requestedDefaults, snapshot);
        });
    }

    public async Task SynchronizeWithScheduleAsync(int positionCount, CancellationToken ct)
    {
        var existing = await _context.ShiftDefaultLeads.ToListAsync(ct);
        var removed = existing.Where(d => d.Position > positionCount).ToList();
        var snapshot = await _fallbackProvider.GetAsync(ct);
        await EnsureNoOperationalDataLeftWithoutLeadAsync(
            removed.Select(d => d.Position), snapshot, ct);

        _context.ShiftDefaultLeads.RemoveRange(removed);
        var remaining = existing.Except(removed)
            .ToDictionary(d => d.Position, d => d.ShiftLeadId);
        await CleanupEmptyDaysWithoutEffectiveLeadAsync(
            remaining, snapshot.FirstActiveManagerId, ct);
    }

    private static Dictionary<int, string?> ValidatePositions(
        SaveShiftDefaultLeadsRequestDto request, int schedulePositionCount)
    {
        if (request.Items == null)
            throw new DomainException("Список позиций старших по умолчанию обязателен");
        if (request.Items.Any(i => i is null))
            throw new DomainException("Позиция старшего по умолчанию не может быть null");

        var expected = Enumerable.Range(1, schedulePositionCount).ToHashSet();
        var positions = request.Items.Select(i => i.Position).ToList();
        if (positions.Count != expected.Count || positions.Distinct().Count() != positions.Count ||
            positions.Any(p => !expected.Contains(p)))
            throw new ShiftConcurrencyException(StalePositionsMessage);

        return request.Items.ToDictionary(i => i.Position, i => i.ShiftLeadId);
    }

    private static void ValidateCandidates(
        IEnumerable<string?> candidateIds, ShiftLeadFallbackSnapshot snapshot)
    {
        foreach (var candidateId in candidateIds.Where(id => id != null).Distinct())
        {
            if (!snapshot.Users.TryGetValue(candidateId!, out var user) || !user.IsActive ||
                (user.Role != UserRole.ShiftLead && user.Role != UserRole.Manager))
                throw new DomainException(
                    "Старшим по умолчанию может быть только активный старший смены или руководитель");
        }
    }

    private async Task EnsureNoOperationalDataLeftWithoutLeadAsync(
        IEnumerable<int> clearedPositions, ShiftLeadFallbackSnapshot snapshot, CancellationToken ct)
    {
        if (snapshot.FirstActiveManagerId != null) return;
        var positions = clearedPositions.Distinct().ToList();
        if (positions.Count == 0) return;

        var hasProtectedItem = await _context.ShiftPlanItems.AsNoTracking()
            .Where(i => positions.Contains(i.Position) && i.ShiftLeadId == null)
            .Where(i => i.Closure == null || i.Closure.State != ShiftClosureState.Closed)
            .Where(i => i.Closure != null || i.OperatorAssignments.Any())
            .AnyAsync(ct);
        if (hasProtectedItem)
            throw new DomainException(
                "Нельзя оставить смену с литейщиками или сменным рапортом без старшего");
    }

    private async Task CleanupEmptyDaysWithoutEffectiveLeadAsync(
        IReadOnlyDictionary<int, string> defaults, string? firstActiveManagerId, CancellationToken ct)
    {
        if (firstActiveManagerId != null) return;

        var candidates = await _context.ShiftPlanDays
            .Include(d => d.Items).ThenInclude(i => i.OperatorAssignments)
            .Include(d => d.Items).ThenInclude(i => i.Closure)
            .Where(d => d.Items.All(i => i.ShiftLeadId == null))
            .Where(d => d.Items.All(i => !i.OperatorAssignments.Any() && i.Closure == null))
            .ToListAsync(ct);
        var emptyDays = candidates
            .Where(d => d.Items.All(i => !defaults.ContainsKey(i.Position)))
            .ToList();
        _context.ShiftPlanDays.RemoveRange(emptyDays);
    }

    private static ShiftDefaultLeadSettingsDto BuildResponse(
        IReadOnlyCollection<Shift> shifts,
        IReadOnlyDictionary<int, string> defaults,
        ShiftLeadFallbackSnapshot snapshot)
    {
        var items = shifts.OrderBy(s => s.StartMinutes).Select((shift, index) =>
        {
            var position = index + 1;
            defaults.TryGetValue(position, out var defaultLeadId);
            snapshot.Users.TryGetValue(defaultLeadId ?? string.Empty, out var defaultLead);
            return new ShiftDefaultLeadItemDto
            {
                Position = position,
                Number = position,
                StartMinutes = shift.StartMinutes,
                DurationMinutes = shift.DurationMinutes,
                BreakStartMinutes = shift.BreakStartMinutes,
                BreakDurationMinutes = shift.BreakDurationMinutes,
                TimeZoneId = shift.TimeZoneId,
                DefaultShiftLeadId = defaultLeadId,
                DefaultShiftLeadName = defaultLead?.FullName,
                DefaultShiftLeadEmployeeId = defaultLead?.EmployeeId
            };
        }).ToList();

        ShiftFallbackManagerDto? fallback = null;
        if (snapshot.FirstActiveManagerId != null &&
            snapshot.Users.TryGetValue(snapshot.FirstActiveManagerId, out var manager))
            fallback = new ShiftFallbackManagerDto
            {
                Id = manager.Id,
                FullName = manager.FullName,
                EmployeeId = manager.EmployeeId,
                CreatedAt = manager.CreatedAt
            };

        return new ShiftDefaultLeadSettingsDto { Items = items, FallbackManager = fallback };
    }
}
