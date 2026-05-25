using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wintime.Control.Core.Entities;
using EntityTaskStatus = Wintime.Control.Core.Enums.TaskStatus;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.DTOs.Mqtt;
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

        var currentMode = data.Mode ?? string.Empty;
        var immId = device.Id;
        var currentTime = DateTimeOffset.FromUnixTimeSeconds(data.Timestamp).UtcDateTime;

        var state = _tracker.Get(immId);

        if (state is null)
        {
            // Первое сообщение — инициализируем состояние
            var startTime = currentMode == "auto" ? currentTime : (DateTime?)null;
            _tracker.Set(immId, new CycleState(startTime, currentCounter, currentMode));
            return;
        }

        bool cycleWasActive = state.CycleStartTime.HasValue;
        bool counterChanged = state.LastCounterValue.HasValue && state.LastCounterValue.Value != currentCounter;
        bool modeChangedFromAuto = state.LastMode == "auto" && currentMode != "auto";

        bool cycleEnded = cycleWasActive && (counterChanged || modeChangedFromAuto);

        if (cycleEnded)
        {
            bool isSuccessful = currentMode != "alarm";
            var cycleStart = state.CycleStartTime!.Value;
            var duration = (int)(currentTime - cycleStart).TotalSeconds;

            // Получить активное задание и пресс-форму
            var activeTask = await _db.Tasks
                .Include(t => t.Mold)
                .FirstOrDefaultAsync(t => t.ImmId == immId && t.Status == EntityTaskStatus.InProgress, ct);

            var cycle = new ImmCycle
            {
                ImmId = immId,
                TaskId = activeTask?.Id,
                MoldId = activeTask?.MoldId,
                StartTime = cycleStart,
                EndTime = currentTime,
                DurationSeconds = duration,
                IsSuccessful = isSuccessful
            };
            _db.ImmCycles.Add(cycle);

            if (isSuccessful && activeTask is not null)
            {
                activeTask.ActualQuantity += activeTask.Mold.Cavities;
                activeTask.ActualMaterialWeightGrams +=
                    activeTask.Mold.Cavities * activeTask.Mold.PartWeightGrams
                    + activeTask.Mold.RunnerWeightGrams;
                if (activeTask.ActualQuantity >= activeTask.PlanQuantity)
                    await _emulator.SetModeAsync(immId.ToString(), "idle", ct);
            }

            await _db.SaveChangesAsync(ct);

            _logger.LogDebug(
                "IMM {ImmId}: cycle saved — duration {Duration}s, successful={Success}",
                immId, duration, isSuccessful);
        }

        // Обновить состояние трекера
        DateTime? newCycleStart = null;
        if (counterChanged && currentMode == "auto")
            newCycleStart = currentTime; // новый цикл начинается сразу после завершённого
        else if (!cycleWasActive && currentMode == "auto")
            newCycleStart = currentTime; // переход из не-auto в auto
        else if (cycleWasActive && !cycleEnded)
            newCycleStart = state.CycleStartTime; // цикл продолжается

        _tracker.Set(immId, new CycleState(newCycleStart, currentCounter, currentMode));
    }
}
