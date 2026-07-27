namespace Wintime.Control.Core.DTOs.Imm;

public class TelemetryDashboardDto
{
    public List<TelemetrySignalDto> Signals { get; set; } = new();
    public List<TelemetryCycleDto> Cycles { get; set; } = new();
    public List<EffectiveStatusSegmentDto> StatusSegments { get; set; } = new();
    public bool Truncated { get; set; }
}

public class TelemetrySignalDto
{
    public string ParameterName { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // float|int|cycleCounter|boolean|string
    public List<TelemetryPointDto> Points { get; set; } = new();
}

public class TelemetryPointDto
{
    public DateTime T { get; set; }
    public decimal? Num { get; set; }
    public string? Txt { get; set; }
}

public class TelemetryCycleDto
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public bool IsSuccessful { get; set; }
}

public class TelemetrySignalMetaDto
{
    public string ParameterName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}
