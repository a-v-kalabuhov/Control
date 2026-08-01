using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;
using EntityTaskStatus = Wintime.Control.Core.Enums.TaskStatus;
using SystemTask = System.Threading.Tasks.Task;

namespace Wintime.Control.Infrastructure.Handlers;

/// <summary>
/// Стадия 2: учёт выпуска и материала активного InProgress-задания по завершённому циклу.
/// Правила — CycleProcessingPolicy.ShouldCountOutput (InProgress + нет простоя;
/// в автомате дополнительно mode == auto, в полуавтомате это условие снято).
/// </summary>
public class TaskOutputHandler : ICycleHandler
{
    private readonly ControlDbContext _db;
    private readonly IEmulatorControlService _emulator;

    public TaskOutputHandler(ControlDbContext db, IEmulatorControlService emulator)
    {
        _db = db;
        _emulator = emulator;
    }

    public async SystemTask HandleAsync(CompletedCycle completed, CancellationToken ct = default)
    {
        var task = completed.ActiveTask;
        var cycle = completed.Cycle;
        if (task is null || !cycle.IsSuccessful)
            return;

        var taskStatus = ActiveTaskStatusMap.From(task.Status);
        bool hasOpenDowntime = await _db.Events.AnyAsync(
            e => e.ImmId == cycle.ImmId
              && e.EventType == Core.Enums.EventType.Downtime
              && e.EndTime == null, ct);

        if (!CycleProcessingPolicy.ShouldCountOutput(completed.Mode, taskStatus, hasOpenDowntime, task.WorkMode))
            return;

        task.ActualQuantity += cycle.Cavities;
        task.ActualMaterialWeightGrams += cycle.Cavities * task.Mold.PartWeightGrams + task.Mold.RunnerWeightGrams;
        if (task.ActualQuantity >= task.PlanQuantity)
            await _emulator.SetModeAsync(cycle.ImmId.ToString(), "idle", ct);

        await _db.SaveChangesAsync(ct);
    }
}
