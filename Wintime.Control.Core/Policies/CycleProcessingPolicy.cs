using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.Policies;

/// <summary>
/// Правила обработки циклов и учёта выпуска по матрице docs/details/Состояния_ТПА.xlsx.
/// Чистые функции: решают, писать ли цикл (ImmCycle) и увеличивать ли выработку задания.
/// </summary>
public static class CycleProcessingPolicy
{
    /// <summary>
    /// Обрабатывать ли смыкание (писать ImmCycle).
    /// InProgress — всегда; нет задания — только при auto; Setup (наладка) — никогда.
    /// </summary>
    public static bool ShouldProcessCycle(string mode, ActiveTaskStatus task) => task switch
    {
        ActiveTaskStatus.InProgress => true,
        ActiveTaskStatus.None => ImmMode.Normalize(mode) == ImmMode.Auto,
        _ => false // Setup
    };

    /// <summary>
    /// Учитывать ли выпуск (ActualQuantity / материал задания).
    /// Общее условие: задание InProgress И нет открытого простоя.
    /// В автомате дополнительно требуется режим auto; в полуавтомате это условие
    /// снято — там ТПА между циклами ждёт оператора, и коннектор по своему таймауту
    /// успевает уйти в idle до завершения цикла.
    /// </summary>
    public static bool ShouldCountOutput(string mode, ActiveTaskStatus task,
                                         bool hasOpenDowntime, WorkMode workMode)
    {
        if (task != ActiveTaskStatus.InProgress || hasOpenDowntime)
            return false;

        return workMode == WorkMode.SemiAuto
            || ImmMode.Normalize(mode) == ImmMode.Auto;
    }
}
