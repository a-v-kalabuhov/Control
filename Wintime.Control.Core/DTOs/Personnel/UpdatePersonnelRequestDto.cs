namespace Wintime.Control.Core.DTOs.Personnel;

public class UpdatePersonnelRequestDto
{
    public string? FullName { get; set; }
    public string? Qualification { get; set; }
    public bool? IsActive { get; set; }
}