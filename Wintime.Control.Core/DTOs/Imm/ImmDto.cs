namespace Wintime.Control.Core.DTOs.Imm;

public class ImmDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? InventoryNumber { get; set; }
    public Guid TemplateId { get; set; }
    public string? Manufacturer { get; set; } // Из шаблона (денормализация)
    public string? Model { get; set; } // Из шаблона (денормализация)
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
}