namespace Wintime.Control.Core.Policies;

/// <summary>
/// Совместимость пресс-формы с ТПА (PZP-16, ADR-0018): ручной список совместимых ТПА в карточке ПФ.
/// Пустой список — совместимость не указана, ПФ подходит к любому ТПА.
/// Чистая функция — вызывающий код сам грузит ID совместимых ТПА.
/// </summary>
public static class MoldImmCompatibility
{
    public static bool IsCompatible(IReadOnlyCollection<Guid> compatibleImmIds, Guid immId)
        => compatibleImmIds.Count == 0 || compatibleImmIds.Contains(immId);
}
