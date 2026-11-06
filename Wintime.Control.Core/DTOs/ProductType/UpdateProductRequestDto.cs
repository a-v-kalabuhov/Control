namespace Wintime.Control.Core.DTOs.ProductType;

public class UpdateProductRequestDto
{
    public string? Article { get; set; }
    public string? Name { get; set; }
    public decimal? PartWeightGrams { get; set; }
    public int? PlannedInjectionCycleSeconds { get; set; }
    /// <summary>Явно очистить необязательный эталон; null у PlannedInjectionCycleSeconds означает «не менять».</summary>
    public bool ClearPlannedInjectionCycleSeconds { get; set; }
    public bool? HasSample { get; set; }                // null — не менять
    public bool? IsActive { get; set; }
}
