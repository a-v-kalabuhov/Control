namespace Wintime.Control.Core.Entities;

public class ShiftDefaultLead
{
    public int Position { get; set; }
    public string ShiftLeadId { get; set; } = string.Empty;
    public User ShiftLead { get; set; } = null!;
}
