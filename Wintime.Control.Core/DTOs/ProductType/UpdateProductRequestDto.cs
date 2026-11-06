namespace Wintime.Control.Core.DTOs.ProductType;

public class UpdateProductRequestDto
{
    public string? Article { get; set; }
    public string? Name { get; set; }
    public string? Comment { get; set; }
    /// <summary>Явно очистить комментарий; null у Comment означает «не менять».</summary>
    public bool ClearComment { get; set; }
    public decimal? PartWeightGrams { get; set; }
    public int? Cavities { get; set; }
    /// <summary>Явно очистить умолчание гнёзд; null у Cavities означает «не менять».</summary>
    public bool ClearCavities { get; set; }
    public int? PlannedInjectionCycleSeconds { get; set; }
    /// <summary>Явно очистить необязательный эталон; null у PlannedInjectionCycleSeconds означает «не менять».</summary>
    public bool ClearPlannedInjectionCycleSeconds { get; set; }
    public bool? HasSample { get; set; }                // null — не менять
    public bool? IsActive { get; set; }
}
