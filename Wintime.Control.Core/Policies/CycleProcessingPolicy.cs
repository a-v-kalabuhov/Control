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
    /// Только: задание InProgress И режим auto И нет открытого простоя.
    /// </summary>
    public static bool ShouldCountOutput(string mode, ActiveTaskStatus task, bool hasOpenDowntime) =>
        task == ActiveTaskStatus.InProgress
        && ImmMode.Normalize(mode) == ImmMode.Auto
        && !hasOpenDowntime;
}
