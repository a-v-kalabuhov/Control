namespace Wintime.Control.Core.Entities;

public class ShiftClosureCorrection : BaseEntity
{
    public Guid ShiftClosureId { get; set; }
    public string ChangedById { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string ChangesJson { get; set; } = string.Empty;
    public ShiftClosure ShiftClosure { get; set; } = null!;
    public User ChangedBy { get; set; } = null!;
}
