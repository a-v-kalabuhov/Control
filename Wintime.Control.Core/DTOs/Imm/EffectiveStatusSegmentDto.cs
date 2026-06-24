namespace Wintime.Control.Core.DTOs.Imm;

public class EffectiveStatusSegmentDto
{
    public string EffectiveStatus { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
    public DateTime? EndedAt { get; set; }
}
