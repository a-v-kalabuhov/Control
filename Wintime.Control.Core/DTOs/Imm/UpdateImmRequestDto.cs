namespace Wintime.Control.Core.DTOs.Imm;

public class UpdateImmRequestDto
{
    public string? Name { get; set; }
    public string? InventoryNumber { get; set; }
    public string? ConnectorAlias { get; set; }
    public DateTime? CommissioningDate { get; set; }
    public bool? IsActive { get; set; }
}