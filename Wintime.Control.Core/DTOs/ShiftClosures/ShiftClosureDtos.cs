namespace Wintime.Control.Core.DTOs.ShiftClosures;

public sealed record ShiftClosureFieldError(Guid TaskId, string Field, string Code);

public sealed record ShiftClosureValidationEntry
{
    public Guid TaskId { get; init; }
    public bool HasOperators { get; init; }
    public bool IsManualFallbackRequired { get; init; }
    public int AutomaticOutputQuantity { get; init; }
    public int? TaskPackagingUnit { get; init; }
    public decimal? ActualPartWeightGrams { get; init; }
    public int? ManualFullCycleSeconds { get; init; }
    public int? ActualPackagingUnit { get; init; }
    public decimal? PackagingPlaces { get; init; }
    public int? ManualGoodQuantity { get; init; }
    public int? ManualDefectQuantity { get; init; }
}
