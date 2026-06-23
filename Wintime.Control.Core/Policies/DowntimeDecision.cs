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
        int thresholdSeconds)
    {
        bool isAuto = rawStatus == ImmStatus.Auto;
        bool productionActive = task == ActiveTaskStatus.InProgress;

        // Закрытие: есть открытый авто-простой и производство возобновилось/прекратилось.
        if (hasOpenAutoDowntime && (isAuto || !productionActive))
            return new DowntimeOutcome(DowntimeAction.Close, statusSinceUtc);

        // Открытие: производство идёт, статус не-Auto дольше порога, открытого простоя нет.
        if (productionActive && !isAuto && !hasOpenAutoDowntime)
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
