namespace Wintime.Control.Core.DTOs.Mold;

public class MoldProductWeightDto
{
    public Guid ProductId { get; set; }
    public string FullArticle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal PartWeightGrams { get; set; }
    public int? Cavities { get; set; }
    public decimal? ShotWeightGrams { get; set; }
}
