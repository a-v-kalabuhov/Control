namespace Wintime.Control.Core.Entities;

public class ShiftImmOperatorAssignment : BaseEntity
{
    public Guid ShiftPlanItemId { get; set; }
    public Guid ImmId { get; set; }
    public string OperatorId { get; set; } = string.Empty;
    public int Slot { get; set; }
    public DateTime AssignedAt { get; set; }
    public DateTime? UnassignedAt { get; set; }
    public string CreatedById { get; set; } = string.Empty;
    public string? UpdatedById { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public ShiftPlanItem ShiftPlanItem { get; set; } = null!;
    public Imm Imm { get; set; } = null!;
    public User Operator { get; set; } = null!;
    public User CreatedBy { get; set; } = null!;
    public User? UpdatedBy { get; set; }
}
