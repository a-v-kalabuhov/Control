namespace Wintime.Control.Core.DTOs.Mold;

public class QrCodeDto
{
    public string EntityType { get; set; } = string.Empty; // mold, machine
    public string EntityId { get; set; } = string.Empty;
    public string QrData { get; set; } = string.Empty;
    public string? QrImageBase64 { get; set; }
}