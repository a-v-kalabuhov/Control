namespace Wintime.Control.Shared.Settings;

public class DowntimeSettings
{
    public const string SectionName = "Downtime";

    /// <summary>
    /// Порог: сколько секунд не-Auto при активном задании считать простоем.
    /// <para>
    /// 900 секунд (15 минут) — намеренно высокое значение, единое для автомата и
    /// полуавтомата. Журнал простоев есть очередь работы для наладчика и материал
    /// для разбора у начальника; записи ценой в минуту простоя обесценивают обе роли.
    /// Короткие остановы не теряются — длительность каждой паузы пишется в ImmCycle
    /// и разбирается аналитикой, без действий человека. Подробности — ADR-0010.
    /// </para>
    /// </summary>
    public int IdleThresholdSeconds { get; set; } = 900;

    /// <summary>Период опроса воркера простоев, секунды.</summary>
    public int PollingIntervalSeconds { get; set; } = 10;
}
