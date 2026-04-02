namespace Wintime.Control.Core.DTOs.Mold;

public class CreateMoldRequestDto
{
    public string Name { get; set; } = string.Empty;
    public int Cavities { get; set; }
    public decimal PartWeightGrams { get; set; }
    public decimal RunnerWeightGrams { get; set; }
    public int MaxResourceCycles { get; set; }
    public int? To1Cycles { get; set; }
    public int? To2Cycles { get; set; }
    public string? StorageLocationIndex { get; set; }
}