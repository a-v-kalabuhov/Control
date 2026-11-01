namespace Wintime.Control.Core.Enums;

/// <summary>
/// Тип материала (PZP-22). Хранится в БД как int (колонка Materials.Type) — значения фиксированы.
/// Новый тип (добавки, регранулят) добавляется значением enum. Тип материала после создания не меняется.
/// </summary>
public enum MaterialType
{
    Plastic = 0,   // Пластик
    Colorant = 1   // Краситель
}
