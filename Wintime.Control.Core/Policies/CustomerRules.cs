namespace Wintime.Control.Core.Policies;

/// <summary>Общие правила необязательного имени заказчика/компании (PZP-23).</summary>
public static class CustomerRules
{
    /// <summary>Trim; пустая строка → <c>null</c>.</summary>
    public static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
