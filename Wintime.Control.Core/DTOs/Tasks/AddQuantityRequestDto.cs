namespace Wintime.Control.Core.DTOs.Tasks;

/// <summary>
/// PZP-08: запрос на увеличение планового количества задания в работе.
/// </summary>
public class AddQuantityRequestDto
{
    /// <summary>Дополнительное количество (прибавляется к плану); должно быть больше нуля.</summary>
    public int Delta { get; set; }
}
