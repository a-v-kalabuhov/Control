using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Wintime.Control.Core.DTOs.Auth;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Auth;
using Wintime.Control.Shared.Settings;
using System.Security.Claims;

namespace Wintime.Control.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly IJwtTokenService _tokenService;
    private readonly JwtSettings _jwtSettings;

    public AuthController(
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        IJwtTokenService tokenService,
        IOptions<JwtSettings> jwtSettings)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokenService = tokenService;
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
        var refreshToken = _tokenService.GenerateRefreshToken();

        // TODO: Сохранить refreshToken в БД для возможности отзыва

        return Ok(new LoginResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes),
            User = new UserProfileDto
            {
                Id = user.Id,
                Login = user.UserName ?? string.Empty,
                FullName = user.FullName,
                Role = user.Role.ToString()
            }
        });
    }

    /// <summary>
    /// Выход из системы
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        // TODO: Добавить refreshToken в blacklist
        return Ok(new { message = "Успешный выход" });
    }

    /// <summary>
    /// Обновление JWT-токена
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponseDto>> Refresh([FromBody] RefreshTokenRequestDto request)
    {
        // Проверяем, что оба токена предоставлены
        if (string.IsNullOrEmpty(request.AccessToken) || string.IsNullOrEmpty(request.RefreshToken))
        {
            return BadRequest(new { message = "Access token и refresh token обязательны" });
        }

        // Получаем пользователя из истёкшего токена
        var principal = _tokenService.GetPrincipalFromExpiredToken(request.AccessToken);
        
        if (principal == null)
        {
            return Unauthorized(new { message = "Недействительный access token" });
        }

        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { message = "Не удалось получить идентификатор пользователя из токена" });
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return Unauthorized(new { message = "Пользователь не найден" });
        }
        
        // TODO: В реальной реализации нужно проверить, что refreshToken действителен и принадлежит пользователю
        // Для этого потребуется хранение refresh токенов в БД с возможностью их отзыва
        // Пока пропускаем эту проверку до реализации механизма хранения refresh токенов

        var newAccessToken = _tokenService.GenerateAccessToken(user);
        var newRefreshToken = _tokenService.GenerateRefreshToken();

        // TODO: Обновить refreshToken в БД (и возможно удалить старый)

        return Ok(new LoginResponseDto
        {
            AccessToken = newAccessToken,
            RefreshToken = newRefreshToken,
            ExpiresAt = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes),
            User = new UserProfileDto
            {
                Id = user.Id,
                Login = user.UserName ?? string.Empty,
                FullName = user.FullName,
                Role = user.Role.ToString()
            }
        });
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

        return Ok(new UserProfileDto
        {
            Id = user.Id,
            Login = user.UserName ?? string.Empty,
            FullName = user.FullName,
            Role = user.Role.ToString()
        });
    }
}