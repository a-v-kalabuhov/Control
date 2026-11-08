namespace Wintime.Control.Core.Entities;

public class ShiftPlanDay : BaseEntity
{
    public DateOnly ShiftDate { get; set; }
    public List<ShiftPlanItem> Items { get; set; } = new();
}
