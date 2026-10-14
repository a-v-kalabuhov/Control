using Wintime.Control.Core.Constants;

namespace Wintime.Control.Core.Policies;

/// <summary>
/// Пауза перед циклом литья (<c>ImmCycle.PauseDurationMs</c>): от раскрытия формы предыдущего
/// цикла ТПА до смыкания нового. Входит в полный период цикла для прогноза выпуска (ADR-0017).
/// <c>null</c> — пауза не отражает нормальную работу и в средний период не входит: нет
/// предыдущего цикла, предыдущий цикл ТПА ещё открыт (решает вызывающий: передаёт previousEndUtc = null),
    /// в паузе был статус кроме <see cref="ImmStatus.Auto"/>/<see cref="ImmStatus.Offline"/>
/// (наладка, простой, авария) или пауза не короче порога простоя (долгий обрыв связи, граница серии).
/// </summary>
public static class CyclePause
{
    /// <summary>Статусы, которые не прерывают паузу: нормальная работа и кратковременный обрыв связи.</summary>
    public static bool IsPauseStatus(string status) =>
        status == ImmStatus.Auto || status == ImmStatus.Offline;

    public static int? Resolve(DateTime? previousEndUtc, DateTime startUtc,
                               bool foreignStatusInPause, int thresholdSeconds)
    {
        if (previousEndUtc is not DateTime prevEnd || foreignStatusInPause)
            return null;

        var pause = startUtc - prevEnd;
        if (pause < TimeSpan.Zero || pause.TotalSeconds >= thresholdSeconds)
            return null;

        return (int)Math.Round(pause.TotalMilliseconds);
    }
}
