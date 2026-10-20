namespace Wintime.Control.Core.Policies;

/// <summary>
/// Нормализация и склейка артикулов (PZP-19). Полный артикул изделия =
/// артикул типа + "." + часть артикула изделия. Точки внутри частей разрешены,
/// поэтому склейка неоднозначна — уникальность полного артикула проверяется отдельно.
/// </summary>
public static class ProductArticle
{
    public const string Separator = ".";
    public const string DefaultPart = "0";
    public const string DefaultName = "По умолчанию";

    public static string Normalize(string? article) =>
        (article ?? string.Empty).Trim().ToUpperInvariant();

    public static string Compose(string typeArticle, string part) =>
        Normalize(typeArticle) + Separator + Normalize(part);
}
