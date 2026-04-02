namespace Wintime.Control.Core.DTOs.Mold;

public class UpdateMoldRequestDto
{
    public string? Name { get; set; }
    public int? Cavities { get; set; }
    public string? StorageLocationIndex { get; set; }
    public bool? IsActive { get; set; }
}