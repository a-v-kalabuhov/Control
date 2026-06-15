namespace Wintime.Control.Core.DTOs.Auth;

/// <summary>
/// Тело запроса выхода из системы. Все поля опциональны — клиенты, которые
/// просто дергают /api/auth/logout без тела, продолжат работать.
/// </summary>
public class LogoutRequestDto
{
    /// <summary>Refresh-токен для отзыва сессии. Если не передан — отзыв пропускается.</summary>
    public string? RefreshToken { get; set; }
}
