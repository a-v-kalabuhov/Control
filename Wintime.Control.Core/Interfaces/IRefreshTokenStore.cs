namespace Wintime.Control.Core.Interfaces;

/// <summary>
/// Запись о выданном refresh-токене. Хранится в store по хешу токена.
/// </summary>
/// <param name="UserId">Идентификатор пользователя, которому принадлежит токен.</param>
/// <param name="ExpiresAtUtc">Момент истечения срока действия (UTC).</param>
/// <param name="RevokedAtUtc">Момент отзыва токена (logout/ротация); <c>null</c> для активного токена.</param>
public sealed record RefreshTokenInfo(string UserId, DateTime ExpiresAtUtc, DateTime? RevokedAtUtc)
{
    /// <summary>Токен активен и срок действия не истёк.</summary>
    public bool IsActive(DateTime nowUtc) => RevokedAtUtc is null && ExpiresAtUtc > nowUtc;
}

/// <summary>
/// In-memory хранилище refresh-токенов (без участия базы данных).
/// </summary>
/// <remarks>
/// Хранит только хеши токенов (SHA-256), никогда — сами значения. Не переживает
/// перезапуск приложения: после рестарта все refresh-сессии считаются
/// недействительными, и пользователи должны войти снова. Это сознательное
/// упрощение для цеховых терминалов, где одновременно активно несколько сессий
/// и потеря refresh-токена при редком рестарте приемлема.
/// </remarks>
public interface IRefreshTokenStore
{
    /// <summary>
    /// Сохраняет выданный refresh-токен и возвращает связанную с ним запись.
    /// </summary>
    /// <param name="token">Открытое значение refresh-токена (внутри хешируется).</param>
    /// <param name="userId">Идентификатор пользователя.</param>
    /// <param name="expiresAtUtc">Срок действия (UTC).</param>
    RefreshTokenInfo Store(string token, string userId, DateTime expiresAtUtc);

    /// <summary>
    /// Проверяет и возвращает запись по токену, если он активен.
    /// </summary>
    /// <returns>Запись для активного токена; иначе <see langword="null"/>.</returns>
    RefreshTokenInfo? GetActive(string token);

    /// <summary>
    /// Помечает токен отозванным (logout или ротация). Не делает ничего для
    /// неизвестного/уже отозванного токена.
    /// </summary>
    void Revoke(string token);

    /// <summary>
    /// Отзывает все активные токены пользователя (например, «выйти отовсюду»).
    /// </summary>
    void RevokeAllForUser(string userId);
}
