namespace Wintime.Control.Core.DTOs.UnplannedRun;

public class UnplannedRunDto
{
    public Guid Id { get; set; }
    public Guid ImmId { get; set; }
    public string ImmName { get; set; } = "";
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }         // деривированный конец (последний цикл)
    public int CycleCount { get; set; }            // деривированное число циклов эпизода
    public double AvgCycleDuration { get; set; }   // сек
    public bool IsClosed { get; set; }
    public Guid? AssignedTaskId { get; set; }
    public string? AssignedTaskLabel { get; set; } // № / ПФ назначенного задания
    public string? PersonnelName { get; set; }     // ФИО наладчика назначенного задания
}
