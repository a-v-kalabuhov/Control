using Wintime.Control.Core.Entities;

namespace Wintime.Control.Core.DTOs.ShiftClosures;

public sealed record ShiftClosureFieldError(Guid TaskId, string Field, string Code);

public sealed record SaveShiftClosureDraftRequest(IReadOnlyList<ShiftClosureManualEntryInput> Entries);

public sealed record CloseShiftClosureRequest(bool ConfirmEarlyClose);

public sealed record ShiftClosureManualEntryInput(
    Guid ShiftTaskId,
    decimal? ActualPartWeightGrams,
    int? ManualFullCycleSeconds,
    int? ActualPackagingUnit,
    decimal? PackagingPlaces,
    int? ManualGoodQuantity,
    int? ManualDefectQuantity);

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

public sealed record ShiftClosureDto
{
    public Guid? Id { get; init; }
    public Guid ShiftPlanItemId { get; init; }
    public ShiftClosureState State { get; init; }
    public DateOnly ShiftDate { get; init; }
    public DateTime StartAtUtc { get; init; }
    public DateTime EndAtUtc { get; init; }
    public DateTime FactEndUtc { get; init; }
    public string TimeZoneId { get; init; } = string.Empty;
    public string ShiftLeadId { get; init; } = string.Empty;
    public string ShiftLeadName { get; init; } = string.Empty;
    public string CreatedById { get; init; } = string.Empty;
    public DateTime? CreatedAt { get; init; }
    public string? ClosedById { get; init; }
    public string? ClosedByName { get; init; }
    public DateTime? ClosedAt { get; init; }
    public bool ClosedEarly { get; init; }
    public List<ShiftClosureEntryDto> Entries { get; init; } = new();
    public List<ShiftClosureTimelineDto> Timelines { get; init; } = new();
}

public sealed class ShiftClosureEntryDto
{
    public Guid TaskId { get; init; }
    public Guid ImmId { get; init; }
    public string ImmName { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public int RemainingAtStart { get; init; }
    public string PlasticIngredients { get; init; } = string.Empty;
    public string ColorantIngredients { get; init; } = string.Empty;
    public decimal NormPartWeightGrams { get; init; }
    public decimal? ActualPartWeightGrams { get; init; }
    public int? PlannedFullCycleSeconds { get; init; }
    public double? AutomaticAverageFullCycleSeconds { get; init; }
    public int? ManualFullCycleSeconds { get; init; }
    public int? TaskPackagingUnit { get; init; }
    public int? ActualPackagingUnit { get; init; }
    public decimal? PackagingPlaces { get; init; }
    public int Cavities { get; init; }
    public int ShiftPlanQuantity { get; init; }
    public decimal PlannedMaterialWeightGrams { get; init; }
    public int AutomaticOutputQuantity { get; init; }
    public int? ManualGoodQuantity { get; init; }
    public int? ManualDefectQuantity { get; init; }
    public bool HasOperators { get; init; }
    public bool IsManualFallbackRequired { get; init; }
    public List<ShiftClosureOutputShareDto> OutputShares { get; init; } = new();
    public List<ShiftClosureOperatorTotalDto> OperatorTotals { get; init; } = new();
}

public sealed class ShiftClosureOutputShareDto
{
    public DateTime IntervalStartUtc { get; init; }
    public DateTime IntervalEndUtc { get; init; }
    public string OperatorId { get; init; } = string.Empty;
    public string OperatorFullName { get; init; } = string.Empty;
    public string? OperatorEmployeeId { get; init; }
    public int SlotAtIntervalStart { get; init; }
    public int Quantity { get; init; }
}

public sealed class ShiftClosureOperatorTotalDto
{
    public string OperatorId { get; init; } = string.Empty;
    public string OperatorFullName { get; init; } = string.Empty;
    public string? OperatorEmployeeId { get; init; }
    public int Quantity { get; init; }
}

public sealed class ShiftClosureTimelineDto
{
    public Guid ImmId { get; init; }
    public string ImmName { get; init; } = string.Empty;
    public List<ShiftClosureTimelineIntervalDto> Intervals { get; init; } = new();
}

public sealed class ShiftClosureTimelineIntervalDto
{
    public DateTime StartAtUtc { get; init; }
    public DateTime EndAtUtc { get; init; }
    public List<ShiftClosureTimelineOperatorDto> Operators { get; init; } = new();
    public int AutomaticOutputQuantity { get; init; }
    public bool HasOrphanOutput { get; init; }
    public List<ShiftClosureTaskOutputDto> TaskOutputs { get; init; } = new();
}

public sealed class ShiftClosureTimelineOperatorDto
{
    public string OperatorId { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string? EmployeeId { get; init; }
    public int Slot { get; init; }
}

public sealed record ShiftClosureTaskOutputDto(Guid TaskId, int Quantity);

public sealed class ShiftClosurePendingDto
{
    public Guid ShiftPlanItemId { get; init; }
    public DateOnly ShiftDate { get; init; }
    public int Position { get; init; }
    public DateTime StartAtUtc { get; init; }
    public DateTime EndAtUtc { get; init; }
    public string TimeZoneId { get; init; } = string.Empty;
    public string ShiftLeadId { get; init; } = string.Empty;
    public string ShiftLeadName { get; init; } = string.Empty;
    public bool HasOrphanOutput { get; init; }
}
