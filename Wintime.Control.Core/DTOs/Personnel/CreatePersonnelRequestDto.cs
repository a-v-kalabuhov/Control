namespace Wintime.Control.Core.DTOs.Personnel;

public class CreatePersonnelRequestDto
{
    public string? EmployeeId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Qualification { get; set; }
    public string Login { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}