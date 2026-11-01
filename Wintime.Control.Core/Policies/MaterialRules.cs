using Wintime.Control.Core.Enums;

namespace Wintime.Control.Core.Policies;

/// <summary>Нормализованные поля материала (PZP-22).</summary>
public sealed record MaterialFields(
    string Article, string Name, string? Manufacturer, string? ColorName, string? ColorCode, string? Comment);

/// <summary>
/// Нормализация и проверка полей материала (PZP-22). Уникальность проверяет контроллер по БД.
/// </summary>
public static class MaterialRules
{
    public const int ArticleMaxLength = 50;
    public const int NameMaxLength = 200;
    public const int ManufacturerMaxLength = 200;
    public const int ColorNameMaxLength = 100;
    public const int ColorCodeMaxLength = 50;
    public const int CommentMaxLength = 1000;

    /// <summary>Trim; пустая строка → <c>null</c>.</summary>
    public static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static MaterialFields Normalize(string? article, string? name, string? manufacturer,
        string? colorName, string? colorCode, string? comment) =>
        new(ProductArticle.Normalize(article),
            (name ?? string.Empty).Trim(),
            Clean(manufacturer),
            Clean(colorName),
            Clean(colorCode),
            Clean(comment));

    public static string? Validate(MaterialType type, MaterialFields f)
    {
        if (f.Article.Length == 0) return "Артикул обязателен.";
        if (f.Name.Length == 0) return "Наименование обязательно.";
        if (type == MaterialType.Colorant && f.ColorName == null) return "Для красителя укажите цвет.";

        return TooLong(f.Article, ArticleMaxLength, "Артикул")
            ?? TooLong(f.Name, NameMaxLength, "Наименование")
            ?? TooLong(f.Manufacturer, ManufacturerMaxLength, "Производитель")
            ?? TooLong(f.ColorName, ColorNameMaxLength, "Цвет")
            ?? TooLong(f.ColorCode, ColorCodeMaxLength, "Код цвета")
            ?? TooLong(f.Comment, CommentMaxLength, "Комментарий");
    }

    public static string TypeLabel(MaterialType type) => type switch
    {
        MaterialType.Plastic => "пластик",
        MaterialType.Colorant => "краситель",
        _ => type.ToString()
    };

    private static string? TooLong(string? value, int max, string label) =>
        value != null && value.Length > max ? $"{label} длиннее {max} символов." : null;
}
