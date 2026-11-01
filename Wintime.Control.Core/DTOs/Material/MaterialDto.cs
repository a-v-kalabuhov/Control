using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.DTOs.Material;

public class MaterialDto
{
    public Guid Id { get; set; }
    public MaterialType Type { get; set; }
    public string Article { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public string? ColorName { get; set; }
    public string? ColorCode { get; set; }
    public string? Comment { get; set; }
    public bool IsActive { get; set; }
    public bool IsUsed { get; set; }   // есть ссылки (рецепты, BL-36) — удалить нельзя, только в архив
}
