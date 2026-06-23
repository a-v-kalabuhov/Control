using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Entities;
using EntityTaskStatus = Wintime.Control.Core.Enums.TaskStatus;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;
using SystemTask = System.Threading.Tasks.Task;

namespace Wintime.Control.Infrastructure.Handlers;

public class CycleProcessingHandler : ICycleProcessingHandler
{
    private readonly ControlDbContext _db;
    private readonly ICycleTracker _tracker;
    private readonly IEmulatorControlService _emulator;
    private readonly ILogger<CycleProcessingHandler> _logger;

    public CycleProcessingHandler(
        ControlDbContext db,
        ICycleTracker tracker,
        IEmulatorControlService emulator,
        ILogger<CycleProcessingHandler> logger)
    {
        _db = db;
        _tracker = tracker;
        _emulator = emulator;
        _logger = logger;
    }

    public async SystemTask ProcessAsync(MqttProcessingContext context, CancellationToken ct = default)
    {
        var data = context.Data;
        var template = context.Template;
        var device = context.Device;

        if (data is null || template is null || device is null)
            return;

        // Найти сенсор счётчика циклов по типу
        var counterSensor = template.Sensors.FirstOrDefault(s => s.ParameterType == "cycleCounter");
        if (counterSensor is null)
            return;

        if (!data.Sensors.TryGetValue(counterSensor.ParameterName, out var rawValue))
            return;

        if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var currentCounter))
            return;

        var currentMode = ImmMode.Normalize(data.Mode);
        var immId = device.Id;
        var currentTime = DateTimeOffset.FromUnixTimeSeconds(data.Timestamp).UtcDateTime;

        // Активное задание: Setup (наладка) или InProgress (производство) — их не более одного.
        var activeTask = await _db.ShiftTasks
            .Include(t => t.Mold)
            .FirstOrDefaultAsync(
                t => t.ImmId == immId
                  && (t.Status == EntityTaskStatus.Setup || t.Status == EntityTaskStatus.InProgress),
                ct);

        var taskStatus = ActiveTaskStatusMap.From(activeTask?.Status);

        // Гейтинг по матрице Состояния_ТПА.xlsx: при Setup и при «нет задания + не-auto»
        // циклы не обрабатываются. Сбрасываем активный цикл в трекере, чтобы он не
        // «склеился» через границу наладки, и сохраняем счётчик/режим.
        if (!CycleProcessingPolicy.ShouldProcessCycle(currentMode, taskStatus))
        {
            _tracker.Set(immId, new CycleState(null, currentCounter, currentMode));
            return;
        }

        var state = _tracker.Get(immId);

        if (state is null)
        {
            var startTime = currentMode == ImmMode.Auto ? currentTime : (DateTime?)null;
            _tracker.Set(immId, new CycleState(startTime, currentCounter, currentMode));
            return;
        }

        bool cycleWasActive = state.CycleStartTime.HasValue;
        bool counterChanged = state.LastCounterValue.HasValue && state.LastCounterValue.Value != currentCounter;
        bool modeChangedFromAuto = state.LastMode == ImmMode.Auto && currentMode != ImmMode.Auto;

        bool cycleEnded = cycleWasActive && (counterChanged || modeChangedFromAuto);

        if (cycleEnded)
        {
            bool isSuccessful = currentMode != ImmMode.Alarm;
            var cycleStart = state.CycleStartTime!.Value;
            var duration = (int)(currentTime - cycleStart).TotalSeconds;

            var cavities = activeTask?.Mold.Cavities ?? 0;

            var cycle = new ImmCycle
            {
                ImmId = immId,
                TaskId = activeTask?.Id,
                MoldId = activeTask?.MoldId,
                StartTime = cycleStart,
                EndTime = currentTime,
                DurationSeconds = duration,
                IsSuccessful = isSuccessful,
                Cavities = cavities
            };
            _db.ImmCycles.Add(cycle);

            // Учёт выпуска — только если политика разрешает (InProgress + auto + нет открытого простоя).
            bool hasOpenDowntime = await _db.Events.AnyAsync(
                e => e.ImmId == immId
                  && e.EventType == Core.Enums.EventType.Downtime
                  && e.EndTime == null, ct);

            if (isSuccessful
                && activeTask is not null
                && CycleProcessingPolicy.ShouldCountOutput(currentMode, taskStatus, hasOpenDowntime))
            {
                activeTask.ActualQuantity += cavities;
                activeTask.ActualMaterialWeightGrams +=
                    cavities * activeTask.Mold.PartWeightGrams
                    + activeTask.Mold.RunnerWeightGrams;
                if (activeTask.ActualQuantity >= activeTask.PlanQuantity)
                    await _emulator.SetModeAsync(immId.ToString(), "idle", ct);
            }

            await _db.SaveChangesAsync(ct);

            _logger.LogDebug(
                "IMM {ImmId}: cycle saved — duration {Duration}s, successful={Success}",
                immId, duration, isSuccessful);
        }

        DateTime? newCycleStart = null;
        if (counterChanged && currentMode == ImmMode.Auto)
            newCycleStart = currentTime;
        else if (!cycleWasActive && currentMode == ImmMode.Auto)
            newCycleStart = currentTime;
        else if (cycleWasActive && !cycleEnded)
            newCycleStart = state.CycleStartTime;

        _tracker.Set(immId, new CycleState(newCycleStart, currentCounter, currentMode));
    }
}
