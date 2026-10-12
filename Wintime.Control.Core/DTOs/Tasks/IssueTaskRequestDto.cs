namespace Wintime.Control.Core.DTOs.Tasks;

/// <summary>Выдача задания; <see cref="PersonnelId"/> — назначить наладчика в той же операции.</summary>
public class IssueTaskRequestDto
{
    public string? PersonnelId { get; set; }
}
