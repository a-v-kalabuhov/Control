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
    /// <summary>
    /// Максимально допустимый разрыв между cycleEnd(N−1) и cycleStart(N), при котором
    /// пауза ещё считается измеренной. Совпадает с порогом простоя из ADR-0010 (900 с):
    /// если машина стояла дольше этого порога (наладка, простой между заданиями), это
    /// уже не пауза оператора между циклами, а отдельный интервал — доверять ей как
    /// PauseDurationMs нельзя (см. ADR-0011).
    /// </summary>
    private const long MaxPauseSpanMs = 900_000L;

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
            long? effectivePrevCycleEndMs = prevCycleEndMs;
            if (cycleStartMs.HasValue && cycleEndMs.HasValue)
            {
                bool notStale = !prevCycleEndMs.HasValue || cycleEndMs.Value > prevCycleEndMs.Value;
                bool notReversed = cycleEndMs.Value >= cycleStartMs.Value;
                bool notOlderThanPrevEnd = !prevCycleEndMs.HasValue || cycleStartMs.Value >= prevCycleEndMs.Value;

                var injectionRawMs = cycleEndMs.Value - cycleStartMs.Value;
                var pauseRawMs = prevCycleEndMs.HasValue ? cycleStartMs.Value - prevCycleEndMs.Value : (long?)null;
                bool injectionFitsInt32 = injectionRawMs >= 0 && injectionRawMs <= int.MaxValue;
                bool pauseFitsInt32 = !pauseRawMs.HasValue || (pauseRawMs.Value >= 0 && pauseRawMs.Value <= int.MaxValue);
                // Шестая защита: разрыв cycleStart(N) − cycleEnd(N−1) не должен превышать порог
                // простоя ADR-0010. Иначе многочасовая наладка/простой между заданиями попал бы
                // в PauseDurationMs как фантомная пауза, хотя все остальные проверки формально
                // проходят (защёлка свежая, не переставлена, в пределах int32).
                bool pauseNotTooOld = !pauseRawMs.HasValue || pauseRawMs.Value <= MaxPauseSpanMs;

                if (notStale && notReversed && notOlderThanPrevEnd && injectionFitsInt32 && pauseFitsInt32)
                {
                    sensorBoundaryValid = true;
                    if (!pauseNotTooOld)
                    {
                        // Пауза не заслуживает доверия, но cycleStart(N)/cycleEnd(N) сами по себе
                        // измерены штатно — впрыск остаётся валидным величиной, доверяем только
                        // ему. Дальше используется тот же код, что и для «первого цикла после
                        // рестарта» (effectivePrevCycleEndMs=null), не отдельная ветка.
                        effectivePrevCycleEndMs = null;
                        _logger.LogWarning(
                            "IMM {ImmId}: rejected: pause span exceeds {ThresholdMs}ms (start={Start}, prevEnd={PrevEnd}, span={SpanMs}ms) — " +
                            "PauseDurationMs set to null, injection duration still measured from sensors",
                            immId, MaxPauseSpanMs, cycleStartMs, prevCycleEndMs, pauseRawMs);
                    }
                }
                else
                {
                    // Называем конкретную провалившуюся защиту в тексте лога (минор #4 финального
                    // ревью, эскалирован) — вместо одного общего "rejected latch" для всех причин.
                    var failedGuards = new List<string>();
                    if (!notStale) failedGuards.Add("stale cycleEnd latch");
                    if (!notReversed) failedGuards.Add("inverted boundaries");
                    if (!notOlderThanPrevEnd) failedGuards.Add("cycleStart precedes previous cycleEnd");
                    if (!injectionFitsInt32 || !pauseFitsInt32) failedGuards.Add("injection/pause span overflows int32");
                    var reason = string.Join("; ", failedGuards);

                    // SemiAuto закрывает каждый физический цикл ДВАЖДЫ: сначала по counterChanged,
                    // затем ещё раз по modeChangedFromAuto (idle) для того же, ещё не сдвинувшегося
                    // латча cycleEnd (см. "Находка 1" в тестах, SemiAuto_counter_then_idle_...).
                    // Это ожидаемо на каждом цикле, а не аномалия — не варт LogWarning-шума.
                    //
                    // Реальный паттерн валит ДВЕ проверки одновременно, не одну: cycleEnd(N) точно
                    // равен prevCycleEndMs (не просто "<=" — это и есть "тот же латч, то же
                    // сообщение"), поэтому notStale проваливается; а поскольку cycleStart(N) не
                    // сдвинулся, а cycleEnd(N) < prevCycleEndMs уже не может быть (латч не идёт
                    // назад), cycleStart(N) < prevCycleEndMs почти всегда — значит и
                    // notOlderThanPrevEnd проваливается тоже. Ключимся на ПРИЧИНУ (точное
                    // совпадение латча + закрытие не по счётчику), а не на количестве
                    // провалившихся проверок — notOlderThanPrevEnd намеренно не требуется здесь,
                    // её провал в этом сценарии ожидаем.
                    //
                    // pauseFitsInt32 НЕ требуется по той же причине: её проверка на неотрицательность
                    // (pauseRawMs = cycleStart(N) − prevCycleEndMs >= 0) — это буквально тот же
                    // предикат, что и notOlderThanPrevEnd, только с обратным знаком. Раз мы терпим
                    // провал notOlderThanPrevEnd, требовать pauseFitsInt32 здесь же означало бы
                    // требовать то же самое условие держаться — снова тупиковый код, как и
                    // Count == 1 до этого фикса. injectionFitsInt32 — независимая защита (впрыск
                    // считается из тех же start/end, что и в исходном валидном закрытии) и
                    // по-прежнему обязана пройти; notReversed — тоже, иначе это не «тот же цикл
                    // ещё раз», а действительно плохие данные.
                    bool isExpectedSemiAutoDoubleClose = prevCycleEndMs.HasValue
                        && cycleEndMs.Value == prevCycleEndMs.Value
                        && !counterChanged
                        && notReversed
                        && injectionFitsInt32;

                    if (isExpectedSemiAutoDoubleClose)
                    {
                        _logger.LogDebug(
                            "IMM {ImmId}: rejected: {Reason} — expected SemiAuto double-close, not an anomaly " +
                            "(start={Start}, end={End}, prevEnd={PrevEnd}) — falling back to message timestamps",
                            immId, reason, cycleStartMs, cycleEndMs, prevCycleEndMs);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "IMM {ImmId}: rejected: {Reason} (start={Start}, end={End}, prevEnd={PrevEnd}) — falling back to message timestamps",
                            immId, reason, cycleStartMs, cycleEndMs, prevCycleEndMs);
                    }
                }
            }

            DateTime cycleStartTime;
            DateTime cycleEndTime;
            int? injectionDurationMs = null;
            int? pauseDurationMs = null;

            if (sensorBoundaryValid)
            {
                cycleEndTime = DateTimeOffset.FromUnixTimeMilliseconds(cycleEndMs!.Value).UtcDateTime;
                cycleStartTime = effectivePrevCycleEndMs.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(effectivePrevCycleEndMs.Value).UtcDateTime
                    : DateTimeOffset.FromUnixTimeMilliseconds(cycleStartMs!.Value).UtcDateTime;
                injectionDurationMs = (int)(cycleEndMs.Value - cycleStartMs!.Value);
                pauseDurationMs = effectivePrevCycleEndMs.HasValue ? (int)(cycleStartMs.Value - effectivePrevCycleEndMs.Value) : null;
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
