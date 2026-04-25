namespace Wintime.Control.Core.Entities;

/// <summary>
/// Причина простоя
/// </summary>
/// <remarks>
/// Причины простоя вводятся в справочнк администратором системы.
/// Одна причина должна быть дефолтная, т.к. система уже зарегистрировала простой, а оператор пока ещё не ввёл причину.
/// <remarks>
public class DowntimeReason : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "Planned"; // Planned, Emergency
    public bool IsActive { get; set; } = true;
    // Navigation
    public ICollection<Event> Events { get; set; } = new List<Event>();
}