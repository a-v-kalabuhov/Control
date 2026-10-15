namespace Wintime.Control.Core.DTOs.Mold;

/// <summary>ТПА из списка совместимых с пресс-формой (PZP-16).</summary>
public class MoldCompatibleImmDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
