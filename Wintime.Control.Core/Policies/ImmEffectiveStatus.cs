using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.Policies;

/// <summary>
/// Чистая функция вычисления эффективного состояния ТПА по матрице
/// docs/details/Состояния_ТПА.xlsx. Переиспользуется live-эндпоинтом дашборда и
/// реконструкцией исторического таймлайна (EffectiveStatusTimeline).
/// </summary>
public static class ImmEffectiveStatus
{
    private static readonly string OfflineMode = ImmMode.Normalize(ImmStatus.Offline);

    public static string Resolve(string rawMode, ActiveTaskStatus task,
                                 bool hasOpenDowntime, bool thresholdPassed)
    {
        var mode = ImmMode.Normalize(rawMode);
        var isAuto = mode == ImmMode.Auto;

        if (task == ActiveTaskStatus.Setup)
            return EffectiveStatus.Setup;

        if (task == ActiveTaskStatus.InProgress)
        {
            if (isAuto) return EffectiveStatus.Production;
            return (hasOpenDowntime || thresholdPassed)
                ? EffectiveStatus.Downtime
                : EffectiveStatus.Production; // до порога — «Stopped» растворён в Работу
        }

        // task == None
        if (isAuto) return EffectiveStatus.Unplanned;
        if (mode == OfflineMode) return EffectiveStatus.Offline;
        return EffectiveStatus.NoTask;
    }
}
