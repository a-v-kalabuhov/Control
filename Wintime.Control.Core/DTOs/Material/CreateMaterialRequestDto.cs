using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.DTOs.Material;

public class CreateMaterialRequestDto
{
    public MaterialType? Type { get; set; }
    public string? Article { get; set; }
    public string? Name { get; set; }
    public string? Manufacturer { get; set; }
    public string? ColorName { get; set; }
    public string? ColorCode { get; set; }
    public string? Comment { get; set; }
}
