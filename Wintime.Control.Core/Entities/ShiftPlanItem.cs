namespace Wintime.Control.Core.Entities;

public class ShiftPlanItem : BaseEntity
{
    public Guid ShiftPlanDayId { get; set; }
    public int Position { get; set; }
    public DateTime StartAtUtc { get; set; }
    public DateTime EndAtUtc { get; set; }
    public string TimeZoneId { get; set; } = "Europe/Moscow";
    public string? ShiftLeadId { get; set; }
    public ShiftPlanDay Day { get; set; } = null!;
    public User? ShiftLead { get; set; }
    public List<ShiftImmOperatorAssignment> OperatorAssignments { get; set; } = new();
}
