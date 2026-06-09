namespace Wintime.Control.Core.DTOs.Imm;

public class CreateImmRequestDto
{
    public string Name { get; set; } = string.Empty;
    public string? InventoryNumber { get; set; }
    public string? ConnectorAlias { get; set; }
    public DateTime? CommissioningDate { get; set; }
    public Guid TemplateId { get; set; }
}