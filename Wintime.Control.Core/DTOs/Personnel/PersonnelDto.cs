namespace Wintime.Control.Core.DTOs.Personnel;

public class PersonnelDto
{
    public string Id { get; set; } = string.Empty;
    public string? EmployeeId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Qualification { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}