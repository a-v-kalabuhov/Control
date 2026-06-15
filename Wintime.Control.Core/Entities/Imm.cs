namespace Wintime.Control.Core.Entities;

/// <summary>
/// ТПА.
/// </summary>
public class Imm : BaseEntity
{
    /// <summary>Наименование (ТПА-05)</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Инвентарный номер</summary>
    public string? InventoryNumber { get; set; }
    /// <summary>Псевдоним в OPC-дереве (например TPA-06)</summary>
    public string? ConnectorAlias { get; set; }
    /// <summary>Дата ввода в эксплуатацию</summary>
    public DateTime? CommissioningDate { get; set; }
    public Guid TemplateId { get; set; }
    public bool IsActive { get; set; } = true;
    // Navigation
    public Template Template { get; set; } = null!;
    public ICollection<ShiftTask> ShiftTasks { get; set; } = new List<ShiftTask>();
    public ICollection<Event> Events { get; set; } = new List<Event>();
    public ICollection<Telemetry> Telemetry { get; set; } = new List<Telemetry>();
}