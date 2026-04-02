namespace Wintime.Control.Core.Entities;

public class Mold : BaseEntity
{
    public string FormId { get; set; } = string.Empty; // Уникальный артикул для QR
    public string Name { get; set; } = string.Empty; // Наименование изделия
    public int Cavities { get; set; } // Гнёздность
    public decimal PartWeightGrams { get; set; } // Вес детали
    public decimal RunnerWeightGrams { get; set; } // Вес литника
    public int MaxResourceCycles { get; set; } // Ресурс
    public int? To1Cycles { get; set; } // Порог ТО1
    public int? To2Cycles { get; set; } // Порог ТО2
    public string? StorageLocationIndex { get; set; } // Индекс места (А-12)
    public string? DrawingPath { get; set; }
    public string? PhotoPath { get; set; }
    public bool IsActive { get; set; } = true;

    // Calculated (not mapped directly, calculated via Usage)
    // public int TotalCycles { get; set; } 

    // Navigation
    public ICollection<Task> Tasks { get; set; } = new List<Task>();
    public ICollection<MoldUsage> Usages { get; set; } = new List<MoldUsage>();
}