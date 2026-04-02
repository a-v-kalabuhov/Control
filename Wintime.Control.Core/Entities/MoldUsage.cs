namespace Wintime.Control.Core.Entities;

// История использования пресс-формы (для учёта ресурса)
public class MoldUsage : BaseEntity
{
    public Guid MoldId { get; set; }
    public Guid ImmId { get; set; }
    public Guid? TaskId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int CyclesStart { get; set; }
    public int CyclesEnd { get; set; }
    public int CyclesCount => CyclesEnd - CyclesStart;

    // Navigation
    public Mold Mold { get; set; } = null!;
    public Imm Imm { get; set; } = null!;
    public Task? Task { get; set; }
}