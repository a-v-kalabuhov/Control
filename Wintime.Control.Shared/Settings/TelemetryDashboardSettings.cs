namespace Wintime.Control.Shared.Settings;

public class TelemetryDashboardSettings
{
    public const string SectionName = "TelemetryDashboard";

    /// <summary>Максимальная ширина окна выборки, часы (гард).</summary>
    public int MaxWindowHours { get; set; } = 4;

    /// <summary>Максимум строк телеметрии на ответ; при превышении — Truncated=true.</summary>
    public int MaxPoints { get; set; } = 50000;
}
