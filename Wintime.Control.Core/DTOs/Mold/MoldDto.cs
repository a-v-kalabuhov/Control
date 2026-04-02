namespace Wintime.Control.Core.DTOs.Mold;

public class MoldDto
{
    public Guid Id { get; set; }
    public string FormId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Cavities { get; set; }
    public decimal PartWeightGrams { get; set; }
    public decimal RunnerWeightGrams { get; set; }
    public int MaxResourceCycles { get; set; }
    public int? To1Cycles { get; set; }
    public int? To2Cycles { get; set; }
    public string? StorageLocationIndex { get; set; }
    public string? DrawingPath { get; set; }
    public string? PhotoPath { get; set; }
    public int TotalCycles { get; set; } // Расчётное
    public int RemainingResource { get; set; } // Расчётное
    public bool IsActive { get; set; }
}