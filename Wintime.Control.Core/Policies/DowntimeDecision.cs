using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.Policies;

public enum DowntimeAction { None, Open, Close }

public readonly record struct DowntimeOutcome(DowntimeAction Action, DateTime At);

/// <summary>
/// Решение об автоматическом простое для одного ТПА на момент опроса воркера.
/// Открывает простой, если задание InProgress и сырой статус не-Auto дольше порога;
/// закрывает открытый авто-простой при возврате в Auto или выходе задания из InProgress.
/// </summary>
public static class DowntimeDecision
{
    public static DowntimeOutcome Evaluate(
        string rawStatus,
        DateTime statusSinceUtc,
        DateTime nowUtc,
        ActiveTaskStatus task,
        DateTime? taskStartedAtUtc,
        bool hasOpenAutoDowntime,
        bool hasOpenManualDowntime,
        int thresholdSeconds)
    {
        bool isAuto = rawStatus == ImmStatus.Auto;
        bool productionActive = task == ActiveTaskStatus.InProgress;

        // Закрытие: только открытый АВТО-простой когда-либо закрывается воркером.
        // Производство возобновилось (isAuto) -> закрываем в statusSinceUtc (момент возврата в Auto).
        // Задание вышло из InProgress, пока всё ещё не в Auto (!productionActive) -> момент выхода
        // не зафиксирован в кеше статусов, поэтому используем nowUtc (никогда не раньше начала простоя).
        if (hasOpenAutoDowntime && (isAuto || !productionActive))
        {
            var endAt = isAuto ? statusSinceUtc : nowUtc;
            return new DowntimeOutcome(DowntimeAction.Close, endAt);
        }

        // Открытие: производство идёт, статус не-Auto дольше порога, и НЕТ открытого простоя
        // ЛЮБОГО вида (ни авто, ни ручного).
        if (productionActive && !isAuto && !hasOpenAutoDowntime && !hasOpenManualDowntime)
        {
            var start = taskStartedAtUtc.HasValue && taskStartedAtUtc.Value > statusSinceUtc
                ? taskStartedAtUtc.Value
                : statusSinceUtc;

            if ((nowUtc - start).TotalSeconds >= thresholdSeconds)
                return new DowntimeOutcome(DowntimeAction.Open, start);
        }

        return new DowntimeOutcome(DowntimeAction.None, default);
    }
}
