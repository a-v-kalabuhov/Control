namespace Wintime.Control.Core.Entities;

public class ShiftClosureOutputShare : BaseEntity
{
    public Guid ShiftClosureEntryId { get; set; }
    public DateTime IntervalStartUtc { get; set; }
    public DateTime IntervalEndUtc { get; set; }
    public string OperatorId { get; set; } = string.Empty;
    public string OperatorFullNameSnapshot { get; set; } = string.Empty;
    public string? OperatorEmployeeIdSnapshot { get; set; }
    public int SlotAtIntervalStart { get; set; }
    public int Quantity { get; set; }
    public ShiftClosureEntry ShiftClosureEntry { get; set; } = null!;
    public User Operator { get; set; } = null!;
}
