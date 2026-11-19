namespace Wintime.Control.Core.Entities;

public class ShiftClosureEntry : BaseEntity
{
    public Guid ShiftClosureId { get; set; }
    public Guid ShiftTaskId { get; set; }
    public Guid ImmId { get; set; }
    public string ImmNameSnapshot { get; set; } = string.Empty;
    public string ProductNameSnapshot { get; set; } = string.Empty;
    public string PlasticIngredientsSnapshot { get; set; } = string.Empty;
    public string ColorantIngredientsSnapshot { get; set; } = string.Empty;
    public int RemainingAtStart { get; set; }
    public decimal NormPartWeightGrams { get; set; }
    public int? PlannedFullCycleSeconds { get; set; }
    public int? TaskPackagingUnit { get; set; }
    public int Cavities { get; set; }
    public int ShiftPlanQuantity { get; set; }
    public decimal PlannedMaterialWeightGrams { get; set; }
    public int AutomaticOutputQuantity { get; set; }
    public double? AutomaticAverageFullCycleSeconds { get; set; }
    public bool IsManualFallbackRequired { get; set; }
    public decimal? ActualPartWeightGrams { get; set; }
    public int? ManualFullCycleSeconds { get; set; }
    public int? ActualPackagingUnit { get; set; }
    public decimal? PackagingPlaces { get; set; }
    public int? ManualGoodQuantity { get; set; }
    public int? ManualDefectQuantity { get; set; }
    public ShiftClosure ShiftClosure { get; set; } = null!;
    public ShiftTask ShiftTask { get; set; } = null!;
    public Imm Imm { get; set; } = null!;
    public List<ShiftClosureOutputShare> OutputShares { get; set; } = new();
}
