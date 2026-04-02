using Microsoft.AspNetCore.Identity;
using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.Entities;

// Используем IdentityUser для безопасного хранения паролей
public class User : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public Wintime.Control.Core.Enums.UserRole Role { get; set; } = Wintime.Control.Core.Enums.UserRole.Observer;
    public string? EmployeeId { get; set; } // Табельный номер
    public string? Qualification { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public ICollection<Task> AssignedTasks { get; set; } = new List<Task>();
    public ICollection<Event> Events { get; set; } = new List<Event>();
}