namespace Wintime.Control.Core.Entities;

public class ImmStatusHistory
{
    public long Id { get; set; }
    public Guid ImmId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
    public DateTime? EndedAt { get; set; }

    public Imm Imm { get; set; } = null!;
}
