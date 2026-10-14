using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Entities;
using EntityTaskStatus = Wintime.Control.Core.Enums.TaskStatus;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Shared.Settings;
using SystemTask = System.Threading.Tasks.Task;

namespace Wintime.Control.Infrastructure.Handlers;

/// <summary>
/// Оркестратор обработки циклов (контракт v2): открывает ImmCycle по currentCycle,
/// закрывает по lastCycle. Идентичность цикла — пара (CycleNumber, StartTime), не
/// один номер (счётчик коннектора легитимно обнуляется между сериями без разрыва связи).
/// </summary>
public class CycleProcessingHandler : ICycleProcessingHandler
{
    private readonly ControlDbContext _db;
    private readonly ICycleTracker _tracker;
    private readonly IEnumerable<ICycleHandler> _handlers;
    private readonly ILogger<CycleProcessingHandler> _logger;
    private readonly DowntimeSettings _downtime;

    public CycleProcessingHandler(
        ControlDbContext db,
        ICycleTracker tracker,
        IEnumerable<ICycleHandler> handlers,
        ILogger<CycleProcessingHandler> logger,
        IOptions<DowntimeSettings> downtime)
    {
        _db = db;
        _tracker = tracker;
        _handlers = handlers;
        _logger = logger;
        _downtime = downtime.Value;
    }

    public async SystemTask ProcessAsync(MqttProcessingContext context, CancellationToken ct = default)
    {
        var data = context.Data;
        var device = context.Device;
        if (data is null || device is null)
            return;

        var currentMode = ImmMode.Normalize(data.Mode);
        var immId = device.Id;

        var activeTask = await _db.ShiftTasks
            .Include(t => t.Mold)
            .FirstOrDefaultAsync(
                t => t.ImmId == immId
                  && (t.Status == EntityTaskStatus.Setup || t.Status == EntityTaskStatus.InProgress),
                ct);

        var taskStatus = ActiveTaskStatusMap.From(activeTask?.Status);
        var shouldProcess = CycleProcessingPolicy.ShouldProcessCycle(currentMode, taskStatus);

        var state = _tracker.Get(immId);

        // 1. Закрыть строку по lastCycle (или создать+закрыть, если старт не видели).
        // Уже отслеживаемый открытый цикл обязан быть закрыт, даже если текущее сообщение
        // пришло в Alarm/без активного задания.
        if (data.LastCycle is { } last)
        {
            ImmCycle? row = null;
            if (state is { OpenCycleId: { } openId } &&
                state.OpenCycleNumber == last.Number && state.OpenCycleStartTime == last.StartTime)
            {
                row = await _db.ImmCycles.FindAsync([openId], ct);
            }

            row ??= await _db.ImmCycles.FirstOrDefaultAsync(
                c => c.ImmId == immId && c.CycleNumber == last.Number && c.StartTime == last.StartTime, ct);

            // Создание новой строки на закрытии (внезапный lastCycle без ранее увиденного
            // currentCycle) гейтится политикой так же, как открытие — наладка/нет задания без
            // auto не должны заводить циклы. Финализация УЖЕ существующей строки — нет: она
            // обязана закрыться независимо от текущего режима/задания (см. комментарий выше).
            if (row is null && !shouldProcess)
            {
                // ничего не делать: ни строки, ни ICycleHandler'ов, ни изменений трекера.
            }
            else if (row is null || row.EndTime is null)
            {
                var isNewRow = row is null;
                if (row is null)
                {
                    row = new ImmCycle
                    {
                        ImmId = immId,
                        TaskId = activeTask?.Id,
                        MoldId = activeTask?.MoldId,
                        StartTime = last.StartTime,
                        CycleNumber = last.Number,
                        Cavities = activeTask?.Mold.Cavities ?? 0,
                        PauseDurationMs = await ResolvePauseAsync(immId, last.StartTime, ct)
                    };
                }

                row.EndTime = last.EndTime;
                row.InjectionStartTime ??= last.InjectionStartTime;
                row.Cushion = last.Cushion;
                row.DurationSeconds = (int)Math.Round((last.EndTime - row.StartTime).TotalSeconds);
                row.InjectionDurationMs = row.InjectionStartTime.HasValue
                    ? (int)(last.EndTime - row.InjectionStartTime.Value).TotalMilliseconds
                    : null;
                row.IsSuccessful = currentMode != ImmMode.Alarm;

                if (isNewRow)
                    _db.ImmCycles.Add(row);

                await _db.SaveChangesAsync(ct); // СТАДИЯ 1 — цикл долговечен

                if (state?.OpenCycleId == row.Id)
                    _tracker.Set(immId, new CycleState(null, null, null));

                // endedByCounter всегда true под контрактом v2 — цикл закрывается только по lastCycle
                // от коннектора, старой развилки "закрыт счётчиком vs закрыт сменой режима" больше нет.
                var completed = new CompletedCycle(row, activeTask, currentMode, true);
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
                            handler.GetType().Name, immId, row.Id);
                    }
                }

                _logger.LogDebug("IMM {ImmId}: cycle {Number}/{StartTime:O} closed — duration {Duration}s, successful={Success}",
                    immId, row.CycleNumber, row.StartTime, row.DurationSeconds, row.IsSuccessful);
            }
            // else: row.EndTime уже заполнен — повторная публикация lastCycle, дедупликация
        }

        // 2-4. Открыть новую строку по currentCycle, если пара (Number, StartTime) новая.
        // Выполняется ПОСЛЕ закрытия по lastCycle: пауза нового цикла считается от конца только
        // что закрытого (сообщение может нести lastCycle=N-1 и currentCycle=N одновременно).
        // Открытие гейтится политикой (наладка/нет задания без auto не должны заводить новых
        // циклов) — закрытие выше НЕ гейтится.
        state = _tracker.Get(immId); // закрытие выше могло сбросить трекер
        if (shouldProcess && data.CurrentCycle is { } current &&
            (state?.OpenCycleNumber != current.Number || state?.OpenCycleStartTime != current.StartTime))
        {
            var existing = await _db.ImmCycles.FirstOrDefaultAsync(
                c => c.ImmId == immId && c.CycleNumber == current.Number && c.StartTime == current.StartTime, ct);

            if (existing is null)
            {
                var cavities = activeTask?.Mold.Cavities ?? 0;
                var pauseMs = await ResolvePauseAsync(immId, current.StartTime, ct);
                var opened = new ImmCycle
                {
                    ImmId = immId,
                    TaskId = activeTask?.Id,
                    MoldId = activeTask?.MoldId,
                    StartTime = current.StartTime,
                    EndTime = null,
                    CycleNumber = current.Number,
                    InjectionStartTime = current.InjectionStartTime,
                    Cavities = cavities,
                    PauseDurationMs = pauseMs,
                    IsSuccessful = true // временно — уточняется при закрытии
                };
                _db.ImmCycles.Add(opened);
                await _db.SaveChangesAsync(ct);
                existing = opened;
                _logger.LogDebug("IMM {ImmId}: cycle {Number}/{StartTime:O} opened", immId, current.Number, current.StartTime);
            }

            state = new CycleState(current.Number, current.StartTime, existing.Id);
            _tracker.Set(immId, state);
        }

        // Вне производственного состояния (наладка/нет задания без auto) трекер не должен
        // держать псевдо-открытый цикл — закрытие выше (если было) уже сбросило его точечно.
        if (!shouldProcess)
            _tracker.Set(immId, new CycleState(null, null, null));
    }

    /// <summary>
    /// Пауза перед циклом, начинающимся в <paramref name="startUtc"/> (ADR-0017). Предыдущий —
    /// последний по <c>StartTime</c> цикл этого ТПА (индекс <c>(ImmId, StartTime)</c>), независимо
    /// от задания; если он ещё открыт — пауза неизвестна (<c>null</c>). «Чужой» статус — интервал
    /// истории, пересекающий паузу.
    /// </summary>
    private async System.Threading.Tasks.Task<int?> ResolvePauseAsync(Guid immId, DateTime startUtc, CancellationToken ct)
    {
        var prevEnd = await _db.ImmCycles
            .Where(c => c.ImmId == immId && c.StartTime < startUtc)
            .OrderByDescending(c => c.StartTime)
            .Select(c => c.EndTime)
            .FirstOrDefaultAsync(ct);
        if (prevEnd is not DateTime prev)
            return null; // предыдущего цикла нет или он ещё открыт

        // Дешёвая проверка без запроса истории статусов.
        if (CyclePause.Resolve(prev, startUtc, false, _downtime.IdleThresholdSeconds) is null)
            return null;

        var foreignStatus = await _db.ImmStatusHistory.AnyAsync(h =>
            h.ImmId == immId
            && h.Status != ImmStatus.Auto && h.Status != ImmStatus.Offline // = !CyclePause.IsPauseStatus
            && h.ChangedAt < startUtc
            && (h.EndedAt == null || h.EndedAt > prev), ct);

        return foreignStatus ? null : CyclePause.Resolve(prev, startUtc, false, _downtime.IdleThresholdSeconds);
    }
}
