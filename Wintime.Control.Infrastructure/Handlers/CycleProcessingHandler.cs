using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.Cache;
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

/// <summary>
/// Оркестратор обработки циклов: детектирует завершение цикла по cycleCounter,
/// СРАЗУ сохраняет ImmCycle (Стадия 1 — долговечность), затем поверх сохранённого
/// цикла последовательно исполняет ICycleHandler (Стадия 2), каждый в try/catch.
/// </summary>
public class CycleProcessingHandler : ICycleProcessingHandler
{
    private readonly ControlDbContext _db;
    private readonly ICycleTracker _tracker;
    private readonly IEnumerable<ICycleHandler> _handlers;
    private readonly ILogger<CycleProcessingHandler> _logger;

    public CycleProcessingHandler(
        ControlDbContext db,
        ICycleTracker tracker,
        IEnumerable<ICycleHandler> handlers,
        ILogger<CycleProcessingHandler> logger)
    {
        _db = db;
        _tracker = tracker;
        _handlers = handlers;
        _logger = logger;
    }

    public async SystemTask ProcessAsync(MqttProcessingContext context, CancellationToken ct = default)
    {
        var data = context.Data;
        var template = context.Template;
        var device = context.Device;
        if (data is null || template is null || device is null)
            return;

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

        var activeTask = await _db.ShiftTasks
            .Include(t => t.Mold)
            .FirstOrDefaultAsync(
                t => t.ImmId == immId
                  && (t.Status == EntityTaskStatus.Setup || t.Status == EntityTaskStatus.InProgress),
                ct);

        var taskStatus = ActiveTaskStatusMap.From(activeTask?.Status);

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

            // Длительности приходят защёлкнутыми: коннектор обновляет их на событиях
            // формы и повторяет в каждом сообщении. Сенсоров нет — поля остаются null.
            int? injectionDurationMs = ReadIntSensor(template, data, "injectionDuration");
            int? pauseDurationMs = ReadIntSensor(template, data, "cyclePause");

            var cycle = new ImmCycle
            {
                ImmId = immId,
                TaskId = activeTask?.Id,
                MoldId = activeTask?.MoldId,
                StartTime = cycleStart,
                EndTime = currentTime,
                DurationSeconds = duration,
                IsSuccessful = isSuccessful,
                Cavities = cavities,
                InjectionDurationMs = injectionDurationMs,
                PauseDurationMs = pauseDurationMs
            };
            _db.ImmCycles.Add(cycle);
            await _db.SaveChangesAsync(ct); // СТАДИЯ 1 — цикл долговечен

            var completed = new CompletedCycle(cycle, activeTask, currentMode, counterChanged);
            foreach (var handler in _handlers) // СТАДИЯ 2
            {
                try
                {
                    await handler.HandleAsync(completed, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Cycle handler {Handler} failed for IMM {ImmId} (cycle {CycleId})",
                        handler.GetType().Name, immId, cycle.Id);
                }
            }

            _logger.LogDebug("IMM {ImmId}: cycle saved — duration {Duration}s, successful={Success}",
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

    /// <summary>
    /// Прочитать целочисленный сенсор по семантическому типу шаблона.
    /// Возвращает <c>null</c>, если сенсор не описан в шаблоне, отсутствует
    /// в сообщении или значение не парсится.
    /// </summary>
    private static int? ReadIntSensor(CachedTemplate template, MqttTelemetryMessage data, string parameterType)
    {
        var sensor = template.Sensors.FirstOrDefault(s => s.ParameterType == parameterType);
        if (sensor is null)
            return null;
        if (!data.Sensors.TryGetValue(sensor.ParameterName, out var raw))
            return null;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}
