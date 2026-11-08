namespace Wintime.Control.Core.Entities;

public class ShiftImmOperatorAssignment : BaseEntity
{
    public Guid ShiftPlanItemId { get; set; }
    public Guid ImmId { get; set; }
    public string OperatorId { get; set; } = string.Empty;
    public int Slot { get; set; }
    public DateTime AssignedAt { get; set; }
    public DateTime? UnassignedAt { get; set; }
    public ShiftPlanItem ShiftPlanItem { get; set; } = null!;
    public Imm Imm { get; set; } = null!;
    public User Operator { get; set; } = null!;
}
