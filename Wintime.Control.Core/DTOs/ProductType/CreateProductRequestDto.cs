namespace Wintime.Control.Core.DTOs.ProductType;

public class CreateProductRequestDto
{
    public string Article { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public decimal PartWeightGrams { get; set; }
    public int? Cavities { get; set; }
    public int? PlannedInjectionCycleSeconds { get; set; }
    public bool HasSample { get; set; }
}
