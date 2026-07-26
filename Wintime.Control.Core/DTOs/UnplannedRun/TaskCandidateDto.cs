namespace Wintime.Control.Core.DTOs.UnplannedRun;

public class TaskCandidateDto
{
    public Guid TaskId { get; set; }
    public string Label { get; set; } = "";        // человекочитаемое: ПФ + план + дата
    public string? PersonnelName { get; set; }
    public bool Recommended { get; set; }
}
