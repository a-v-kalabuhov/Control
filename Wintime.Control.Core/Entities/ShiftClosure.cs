namespace Wintime.Control.Core.Entities;

public enum ShiftClosureState
{
    Draft = 0,
    Closed = 1
}

public class ShiftClosure : BaseEntity
{
    public Guid ShiftPlanItemId { get; set; }
    public ShiftClosureState State { get; set; }
    public string CreatedById { get; set; } = string.Empty;
    public string? ClosedById { get; set; }
    public DateTime? ClosedAt { get; set; }
    public ShiftPlanItem ShiftPlanItem { get; set; } = null!;
    public User CreatedBy { get; set; } = null!;
    public User? ClosedBy { get; set; }
    public List<ShiftClosureEntry> Entries { get; set; } = new();
    public List<ShiftClosureCorrection> Corrections { get; set; } = new();
}
