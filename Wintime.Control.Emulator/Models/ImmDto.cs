namespace Wintime.Control.Emulator.Models;

/// <summary>
/// DTO для основных свойств IMM.
/// </summary>
public class ImmDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string TemplateId { get; set; } = "";
    /// <summary>
    /// IMM может быть помечена как неактивная, в этом случае её можно не эмулировать.
    /// Т.к. она выведена из работы.
    /// </summary>
    public bool IsActive { get; set; }
}