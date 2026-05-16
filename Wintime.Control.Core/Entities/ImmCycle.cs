namespace Wintime.Control.Core.Entities;

public class ImmCycle : BaseEntity
{
    public Guid ImmId { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? MoldId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int DurationSeconds { get; set; }
    public bool IsSuccessful { get; set; }

    // Navigation
    public Imm Imm { get; set; } = null!;
    public ShiftTask? Task { get; set; }
    public Mold? Mold { get; set; }
}
