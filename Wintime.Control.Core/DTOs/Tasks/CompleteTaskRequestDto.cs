namespace Wintime.Control.Core.DTOs.Tasks;

public class CompleteTaskRequestDto
{
    public int? ActualQuantity { get; set; }
    public int? DefectQuantity { get; set; }
    public string? CompletionReason { get; set; }
}