namespace Wintime.Control.Core.Entities;

public class Imm : BaseEntity
{
    public string Name { get; set; } = string.Empty; // Наименование (ТПА-05)
    public string? InventoryNumber { get; set; } // Инвентарный номер
    public Guid TemplateId { get; set; }
    public bool IsActive { get; set; } = true;

    // Navigation
    public Template Template { get; set; } = null!;
    public ICollection<Task> Tasks { get; set; } = new List<Task>();
    public ICollection<MoldUsage> MoldUsages { get; set; } = new List<MoldUsage>();
    public ICollection<Event> Events { get; set; } = new List<Event>();
    public ICollection<Telemetry> Telemetry { get; set; } = new List<Telemetry>();
}