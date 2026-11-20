namespace Wintime.Control.Core.Entities;

/// <summary>
/// Настройки безопасности (ADR-0015): одна строка, <c>Id = 1</c>.
/// Изменения применяются к последующим проверкам паролей.
/// </summary>
public class SecuritySettings
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public bool AllowWeakPasswords { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedById { get; set; }

    public void Update(bool allowWeakPasswords, string? userId, DateTime nowUtc)
    {
        AllowWeakPasswords = allowWeakPasswords;
        UpdatedAt = nowUtc;
        UpdatedById = userId;
    }
}
