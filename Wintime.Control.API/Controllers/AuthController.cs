using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Wintime.Control.Core.DTOs.Auth;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Auth;
using Wintime.Control.Shared.Settings;
using System.Security.Claims;

namespace Wintime.Control.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly UserManager<User> _userManager;
    private readonly IJwtTokenService _tokenService;
    private readonly IRefreshTokenStore _refreshTokenStore;
    private readonly JwtSettings _jwtSettings;

    public AuthController(
        UserManager<User> userManager,
        IJwtTokenService tokenService,
        IRefreshTokenStore refreshTokenStore,
        IOptions<JwtSettings> jwtSettings)
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _refreshTokenStore = refreshTokenStore;
        _jwtSettings = jwtSettings.Value;
    }

    /// <summary>
    /// Вход в систему
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponseDto>> Login([FromBody] LoginRequestDto request)
    {
        var user = await _userManager.FindByNameAsync(request.Login);

        if (user == null || !await _userManager.CheckPasswordAsync(user, request.Password))
        {
            return Unauthorized(new { message = "Неверный логин или пароль" });
        }

        if (!user.IsActive)
        {
            return Unauthorized(new { message = "Учётная запись деактивирована" });
        }

        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshToken = await IssueRefreshTokenAsync(user.Id);

        return Ok(BuildLoginResponse(user, accessToken, refreshToken));
    }

    /// <summary>
    /// Выход из системы
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public IActionResult Logout([FromBody] LogoutRequestDto? request = null)
    {
        // Отзываем refresh-токен, если клиент его передал. Сам access-токен
        // остаётся действующим до истечения срока (stateless JWT) — это
        // известное ограничение JWT, отзыв access-токена требует blacklist,
        // который здесь не реализован.
        if (!string.IsNullOrEmpty(request?.RefreshToken))
            _refreshTokenStore.Revoke(request.RefreshToken);

        return Ok(new { message = "Успешный выход" });
    }

    /// <summary>
    /// Обновление JWT-токена
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponseDto>> Refresh([FromBody] RefreshTokenRequestDto request)
    {
        if (string.IsNullOrEmpty(request.AccessToken) || string.IsNullOrEmpty(request.RefreshToken))
        {
            return BadRequest(new { message = "Access token и refresh token обязательны" });
        }

        // Шаг 1: access-токен должен быть подписан нами (проверка подписи без
        // проверки срока — он к этому моменту мог истечь, это нормально).
        var principal = _tokenService.GetPrincipalFromExpiredToken(request.AccessToken);
        if (principal == null)
        {
            return Unauthorized(new { message = "Недействительный access token" });
        }

        // Шаг 2: refresh-токен должен быть активным в нашем хранилище.
        var refreshInfo = _refreshTokenStore.GetActive(request.RefreshToken);
        if (refreshInfo == null)
        {
            // Токен неизвестен, отозван или истёк. Возможная причина атаки —
            // попытка повторного использования уже отозванного токена.
            return Unauthorized(new { message = "Недействительный или истёкший refresh token" });
        }

        // Шаг 3: access-токен и refresh-токен должны принадлежать одному пользователю.
        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId) || userId != refreshInfo.UserId)
        {
            return Unauthorized(new { message = "Токены принадлежат разным пользователям" });
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null || !user.IsActive)
        {
            return Unauthorized(new { message = "Пользователь не найден или деактивирован" });
        }

        // Ротация: старый refresh-токен отзывается, выдаётся новая пара.
        _refreshTokenStore.Revoke(request.RefreshToken);
        var newAccessToken = _tokenService.GenerateAccessToken(user);
        var newRefreshToken = await IssueRefreshTokenAsync(user.Id);

        return Ok(BuildLoginResponse(user, newAccessToken, newRefreshToken));
    }

    /// <summary>
    /// Получить текущий профиль пользователя
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserProfileDto>> GetCurrentUser()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
            return NotFound();

        return Ok(BuildUserProfile(user));
    }

    private Task<string> IssueRefreshTokenAsync(string userId)
    {
        var refreshToken = _tokenService.GenerateRefreshToken();
        var expiresAtUtc = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays);
        _refreshTokenStore.Store(refreshToken, userId, expiresAtUtc);
        return Task.FromResult(refreshToken);
    }

    private LoginResponseDto BuildLoginResponse(User user, string accessToken, string refreshToken)
    {
        return new LoginResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes),
            User = BuildUserProfile(user)
        };
    }

    private static UserProfileDto BuildUserProfile(User user)
    {
        return new UserProfileDto
        {
            Id = user.Id,
            Login = user.UserName ?? string.Empty,
            FullName = user.FullName,
            Role = user.Role.ToString()
        };
    }
}