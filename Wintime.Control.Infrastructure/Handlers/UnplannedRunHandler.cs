using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Shared.Settings;
using SystemTask = System.Threading.Tasks.Task;

namespace Wintime.Control.Infrastructure.Handlers;

/// <summary>
/// Стадия 2: если завершённый цикл — сирота (нет активного задания), открывает
/// эпизод «работы без задания» для ТПА, если открытого ещё нет, либо если с конца
/// последней сироты прошло больше порога простоя (старый эпизод закрывается). Продление и счётчик
/// НЕ хранятся — деривятся из ImmCycles (derive-on-read).
/// </summary>
public class UnplannedRunHandler : ICycleHandler
{
    private readonly ControlDbContext _db;
    private readonly DowntimeSettings _downtime;

    public UnplannedRunHandler(ControlDbContext db, IOptions<DowntimeSettings> downtime)
    {
        _db = db;
        _downtime = downtime.Value;
    }

    public async SystemTask HandleAsync(CompletedCycle completed, CancellationToken ct = default)
    {
        if (completed.ActiveTask is not null)
            return; // не сирота

        var cycle = completed.Cycle;
        var immId = cycle.ImmId;
        var openRun = await _db.UnplannedRuns
            .FirstOrDefaultAsync(r => r.ImmId == immId && r.ClosedAt == null, ct);

        if (openRun is not null)
        {
            // Простой дольше порога между сиротами — граница эпизода: закрываем старый
            // концом его последнего цикла и открываем новый (ADR-0013).
            // Текущий цикл уже сохранён стадией 1 — исключаем его.
            var lastEnd = await _db.ImmCycles
                .Where(c => c.ImmId == immId && c.TaskId == null && c.Id != cycle.Id
                    && c.EndTime != null && c.EndTime >= openRun.StartTime)
                .MaxAsync(c => c.EndTime, ct) ?? openRun.StartTime;

            if ((cycle.StartTime - lastEnd).TotalSeconds <= _downtime.IdleThresholdSeconds)
                return; // эпизод продолжается

            openRun.ClosedAt = lastEnd;
        }

        _db.UnplannedRuns.Add(new UnplannedRun { ImmId = immId, StartTime = cycle.EndTime!.Value });
        await _db.SaveChangesAsync(ct);
    }
}
