using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using SystemTask = System.Threading.Tasks.Task;

namespace Wintime.Control.Infrastructure.Handlers;

/// <summary>
/// Стадия 2: если завершённый цикл — сирота (нет активного задания), открывает
/// эпизод «работы без задания» для ТПА, если открытого ещё нет. Продление и счётчик
/// НЕ хранятся — деривятся из ImmCycles (derive-on-read).
/// </summary>
public class UnplannedRunHandler : ICycleHandler
{
    private readonly ControlDbContext _db;

    public UnplannedRunHandler(ControlDbContext db) => _db = db;

    public async SystemTask HandleAsync(CompletedCycle completed, CancellationToken ct = default)
    {
        if (completed.ActiveTask is not null)
            return; // не сирота

        var immId = completed.Cycle.ImmId;
        bool hasOpen = await _db.UnplannedRuns.AnyAsync(r => r.ImmId == immId && r.ClosedAt == null, ct);
        if (hasOpen)
            return;

        _db.UnplannedRuns.Add(new UnplannedRun { ImmId = immId, StartTime = completed.Cycle.EndTime });
        await _db.SaveChangesAsync(ct);
    }
}
