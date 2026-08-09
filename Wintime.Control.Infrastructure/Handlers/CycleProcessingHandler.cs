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

        // 1. Открыть новую строку по currentCycle, если пара (Number, StartTime) новая.
        // Открытие гейтится политикой (наладка/нет задания без auto не должны заводить новых
        // циклов) — закрытие ниже НЕ гейтится: уже отслеживаемый открытый цикл обязан быть
        // закрыт, даже если текущее сообщение пришло в Alarm/без активного задания.
        if (shouldProcess && data.CurrentCycle is { } current &&
            (state?.OpenCycleNumber != current.Number || state?.OpenCycleStartTime != current.StartTime))
        {
            var existing = await _db.ImmCycles.FirstOrDefaultAsync(
                c => c.ImmId == immId && c.CycleNumber == current.Number && c.StartTime == current.StartTime, ct);

            if (existing is null)
            {
                var cavities = activeTask?.Mold.Cavities ?? 0;
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

        // 2-4. Закрыть строку по lastCycle (или создать+закрыть, если старт не видели)
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
                row ??= new ImmCycle
                {
                    ImmId = immId,
                    TaskId = activeTask?.Id,
                    MoldId = activeTask?.MoldId,
                    StartTime = last.StartTime,
                    CycleNumber = last.Number,
                    Cavities = activeTask?.Mold.Cavities ?? 0
                };

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

        // Вне производственного состояния (наладка/нет задания без auto) трекер не должен
        // держать псевдо-открытый цикл — закрытие выше (если было) уже сбросило его точечно.
        if (!shouldProcess)
            _tracker.Set(immId, new CycleState(null, null, null));
    }
}
