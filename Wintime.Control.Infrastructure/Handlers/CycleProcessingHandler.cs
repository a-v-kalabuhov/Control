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
        var currentTime = data.TimestampUtc;

        var activeTask = await _db.ShiftTasks
            .Include(t => t.Mold)
            .FirstOrDefaultAsync(
                t => t.ImmId == immId
                  && (t.Status == EntityTaskStatus.Setup || t.Status == EntityTaskStatus.InProgress),
                ct);

        var taskStatus = ActiveTaskStatusMap.From(activeTask?.Status);

        var state = _tracker.Get(immId);

        if (!CycleProcessingPolicy.ShouldProcessCycle(currentMode, taskStatus))
        {
            _tracker.Set(immId, new CycleState(null, currentCounter, currentMode, state?.LastCycleEndMs));
            return;
        }

        if (state is null)
        {
            var startTime = currentMode == ImmMode.Auto ? currentTime : (DateTime?)null;
            _tracker.Set(immId, new CycleState(startTime, currentCounter, currentMode, null));
            return;
        }

        bool cycleWasActive = state.CycleStartTime.HasValue;
        bool counterChanged = state.LastCounterValue.HasValue && state.LastCounterValue.Value != currentCounter;
        bool modeChangedFromAuto = state.LastMode == ImmMode.Auto && currentMode != ImmMode.Auto;
        bool cycleEnded = cycleWasActive && (counterChanged || modeChangedFromAuto);

        long? newLastCycleEndMs = state.LastCycleEndMs;

        if (cycleEnded)
        {
            bool isSuccessful = currentMode != ImmMode.Alarm;
            var fallbackCycleStart = state.CycleStartTime!.Value;
            var cavities = activeTask?.Mold.Cavities ?? 0;

            var cycleStartMs = ReadLongSensor(template, data, "cycleStart");
            var cycleEndMs = ReadLongSensor(template, data, "cycleEnd");
            var prevCycleEndMs = state.LastCycleEndMs;

            bool sensorBoundaryValid = false;
            if (cycleStartMs.HasValue && cycleEndMs.HasValue)
            {
                bool notStale = !prevCycleEndMs.HasValue || cycleEndMs.Value > prevCycleEndMs.Value;
                bool notReversed = cycleEndMs.Value >= cycleStartMs.Value;

                if (notStale && notReversed)
                {
                    sensorBoundaryValid = true;
                }
                else
                {
                    _logger.LogWarning(
                        "IMM {ImmId}: rejected cycleStart/cycleEnd latch (start={Start}, end={End}, prevEnd={PrevEnd}) — falling back to message timestamps",
                        immId, cycleStartMs, cycleEndMs, prevCycleEndMs);
                }
            }

            DateTime cycleStartTime;
            DateTime cycleEndTime;
            int? injectionDurationMs = null;
            int? pauseDurationMs = null;

            if (sensorBoundaryValid)
            {
                cycleEndTime = DateTimeOffset.FromUnixTimeMilliseconds(cycleEndMs!.Value).UtcDateTime;
                cycleStartTime = prevCycleEndMs.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(prevCycleEndMs.Value).UtcDateTime
                    : DateTimeOffset.FromUnixTimeMilliseconds(cycleStartMs!.Value).UtcDateTime;
                injectionDurationMs = (int)(cycleEndMs.Value - cycleStartMs!.Value);
                pauseDurationMs = prevCycleEndMs.HasValue ? (int)(cycleStartMs.Value - prevCycleEndMs.Value) : null;
                newLastCycleEndMs = cycleEndMs.Value;
            }
            else
            {
                cycleStartTime = fallbackCycleStart;
                cycleEndTime = currentTime;
            }

            var duration = (int)Math.Round((cycleEndTime - cycleStartTime).TotalSeconds);

            var cycle = new ImmCycle
            {
                ImmId = immId,
                TaskId = activeTask?.Id,
                MoldId = activeTask?.MoldId,
                StartTime = cycleStartTime,
                EndTime = cycleEndTime,
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

        _tracker.Set(immId, new CycleState(newCycleStart, currentCounter, currentMode, newLastCycleEndMs));
    }

    /// <summary>
    /// Прочитать целочисленный (64-битный) сенсор по семантическому типу шаблона.
    /// Возвращает <c>null</c>, если сенсор не описан в шаблоне, отсутствует
    /// в сообщении или значение не парсится.
    /// </summary>
    private static long? ReadLongSensor(CachedTemplate template, MqttTelemetryMessage data, string parameterType)
    {
        var sensor = template.Sensors.FirstOrDefault(s => s.ParameterType == parameterType);
        if (sensor is null)
            return null;
        if (!data.Sensors.TryGetValue(sensor.ParameterName, out var raw))
            return null;
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}
