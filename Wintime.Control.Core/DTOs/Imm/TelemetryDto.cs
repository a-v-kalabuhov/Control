namespace Wintime.Control.Core.DTOs.Imm;

public class TelemetryDto
{
    public DateTime Timestamp { get; set; }
    public string ParameterName { get; set; } = string.Empty;
    public decimal? ValueNumeric { get; set; }
    public string? ValueText { get; set; }
}