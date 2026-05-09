namespace Wintime.Control.Core.DTOs.Imm;

public class ImmStatusSegmentDto
{
    public string Status { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
    public DateTime? EndedAt { get; set; }
}
