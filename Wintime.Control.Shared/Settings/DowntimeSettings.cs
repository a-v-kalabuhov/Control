namespace Wintime.Control.Shared.Settings;

public class DowntimeSettings
{
    public const string SectionName = "Downtime";

    /// <summary>Порог: сколько секунд не-Auto при активном задании считать простоем.</summary>
    public int IdleThresholdSeconds { get; set; } = 120;

    /// <summary>Период опроса воркера простоев, секунды.</summary>
    public int PollingIntervalSeconds { get; set; } = 10;
}
