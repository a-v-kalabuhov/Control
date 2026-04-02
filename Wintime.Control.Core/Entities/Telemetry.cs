namespace Wintime.Control.Core.Entities;

// Таблица для телеметрии (COV-фильтрация применяется перед записью)
public class Telemetry
{
    public long Id { get; set; } // Bigint для производительности
    public Guid ImmId { get; set; }
    public DateTime Timestamp { get; set; }
    public string ParameterName { get; set; } = string.Empty;
    public decimal? ValueNumeric { get; set; }
    public string? ValueText { get; set; }

    // Navigation
    public Imm Imm { get; set; } = null!;
}