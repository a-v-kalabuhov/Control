namespace Wintime.Control.Core.DTOs.Tasks;

/// <summary>Назначить наладчика; <c>null</c> — снять (только в «Планируется»).</summary>
public class SetTaskPersonnelRequestDto
{
    public string? PersonnelId { get; set; }
}
