using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.Personnel;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Shared.Constants;

namespace Wintime.Control.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PersonnelController : ControllerBase
{
    private readonly UserManager<User> _userManager;
    private readonly IRefreshTokenStore _refreshTokenStore;

    public PersonnelController(UserManager<User> userManager, IRefreshTokenStore refreshTokenStore)
    {
        _userManager = userManager;
        _refreshTokenStore = refreshTokenStore;
    }

    /// <summary>
    /// Список персонала
    /// </summary>
    [HttpGet]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager},{Roles.Observer}")]
    public async Task<ActionResult<IEnumerable<PersonnelDto>>> GetPersonnelList(
        [FromQuery] bool? isActive = null,
        [FromQuery] string? role = null)
    {
        var query = _userManager.Users.AsQueryable();

        if (isActive.HasValue)
            query = query.Where(u => u.IsActive == isActive.Value);
        if (!string.IsNullOrEmpty(role))
            query = query.Where(u => u.Role.ToString() == role);

        var users = await query.ToListAsync();

        // Observer читает список только ради фильтра «Наладчик» (PZP-07a) — логины ему не отдаём.
        var showLogin = !User.IsInRole(Roles.Observer);

        var dtos = users.Select(u => new PersonnelDto
        {
            Id = u.Id,
            EmployeeId = u.EmployeeId,
            FullName = u.FullName,
            Qualification = u.Qualification,
            Login = showLogin ? u.UserName : null,
            Role = u.Role.ToString(),
            IsActive = u.IsActive
        }).ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// Детали сотрудника по ID
    /// </summary>
    [HttpGet("{id}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<PersonnelDto>> GetPersonnelById(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
            return NotFound();

        var dto = new PersonnelDto
        {
            Id = user.Id,
            EmployeeId = user.EmployeeId,
            FullName = user.FullName,
            Qualification = user.Qualification,
            Login = user.UserName,
            Role = user.Role.ToString(),
            IsActive = user.IsActive
        };

        return Ok(dto);
    }

    /// <summary>
    /// Создать запись персонала
    /// </summary>
    [HttpPost]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<PersonnelDto>> CreatePersonnel([FromBody] CreatePersonnelRequestDto request)
    {
        var login = NormalizeLogin(request.Login);
        var password = NormalizePassword(request.Password);
        if (login != null && password == null)
            return BadRequest("При указании логина пароль обязателен");
        if (login == null && password != null)
            return BadRequest("Пароль нельзя задать без логина");

        var user = new User
        {
            UserName = login,
            Email = login == null ? null : $"{login}@control.local",
            FullName = request.FullName,
            EmployeeId = request.EmployeeId,
            Qualification = request.Qualification,
            IsActive = true
        };

        // Парсинг роли; роль Admin выдаёт только администратор
        if (!Enum.TryParse<Core.Enums.UserRole>(request.Role, out var role))
            return BadRequest("Неверная роль");

        if (role == Core.Enums.UserRole.Admin && !User.IsInRole(Roles.Admin))
            return Forbid();

        user.Role = role;

        var result = login == null
            ? await _userManager.CreateAsync(user)
            : await _userManager.CreateAsync(user, password!);
        if (!result.Succeeded)
            return BadRequest(result.Errors);

        var dto = new PersonnelDto
        {
            Id = user.Id,
            EmployeeId = user.EmployeeId,
            FullName = user.FullName,
            Qualification = user.Qualification,
            Login = user.UserName,
            Role = user.Role.ToString(),
            IsActive = user.IsActive
        };

        return CreatedAtAction(nameof(GetPersonnelList), new { }, dto);
    }

    /// <summary>
    /// Обновить данные персонала
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<IActionResult> UpdatePersonnel(string id, [FromBody] UpdatePersonnelRequestDto request)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
            return NotFound();

        // Руководитель управляет персоналом, но не администраторами: не может ни менять
        // учётку админа, ни выдать роль Admin (иначе повысил бы себя или другого).
        var callerIsAdmin = User.IsInRole(Roles.Admin);
        if (!callerIsAdmin && user.Role == Core.Enums.UserRole.Admin)
            return Forbid();

        Core.Enums.UserRole? newRole = null;
        if (request.Role != null)
        {
            if (!Enum.TryParse<Core.Enums.UserRole>(request.Role, out var parsedRole))
                return BadRequest("Неверная роль");
            if (parsedRole == Core.Enums.UserRole.Admin && !callerIsAdmin)
                return Forbid();
            newRole = parsedRole;
        }

        var loginSpecified = request.Login != null;
        var originalLogin = user.UserName;
        var targetLogin = loginSpecified ? NormalizeLogin(request.Login) : originalLogin;
        var password = NormalizePassword(request.Password);

        if (targetLogin == null && password != null)
            return BadRequest("Пароль нельзя задать без логина");
        if (targetLogin != null && string.IsNullOrEmpty(user.PasswordHash) && password == null)
            return BadRequest("При включении входа пароль обязателен");

        var removesAdminAccess = user.Role == Core.Enums.UserRole.Admin
            && user.IsActive
            && !string.IsNullOrWhiteSpace(originalLogin)
            && ((loginSpecified && targetLogin == null)
                || request.IsActive == false
                || (newRole.HasValue && newRole.Value != Core.Enums.UserRole.Admin));
        if (removesAdminAccess && !await AnotherActiveAdminWithLoginExistsAsync(user.Id))
            return BadRequest("Нельзя отозвать вход, деактивировать или понизить роль последнего активного администратора с доступом");

        if (password != null)
        {
            var passwordResult = await ValidatePasswordAsync(user, password);
            if (!passwordResult.Succeeded)
                return BadRequest(passwordResult.Errors);
        }

        if (request.FullName != null)
            user.FullName = request.FullName;
        if (request.Qualification != null)
            user.Qualification = request.Qualification;
        if (request.IsActive.HasValue)
            user.IsActive = request.IsActive.Value;
        if (newRole.HasValue)
            user.Role = newRole.Value;

        if (loginSpecified)
        {
            user.UserName = targetLogin;
            user.NormalizedUserName = targetLogin == null ? null : _userManager.NormalizeName(targetLogin);
            user.Email = targetLogin == null ? null : $"{targetLogin}@control.local";
            user.NormalizedEmail = targetLogin == null ? null : _userManager.NormalizeEmail(user.Email);
            if (targetLogin == null)
                user.PasswordHash = null;
        }

        if (password != null)
            user.PasswordHash = _userManager.PasswordHasher.HashPassword(user, password);

        if ((loginSpecified && !string.Equals(originalLogin, targetLogin, StringComparison.Ordinal))
            || password != null)
        {
            user.SecurityStamp = Guid.NewGuid().ToString();
        }

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
            return BadRequest(result.Errors);

        // Stateless access JWT остаётся действительным до истечения (до 60 минут),
        // но продлить сессию после очистки логина уже нельзя.
        if (loginSpecified && targetLogin == null)
            _refreshTokenStore.RevokeAllForUser(user.Id);

        return NoContent();
    }

    private static string? NormalizeLogin(string? login) =>
        string.IsNullOrWhiteSpace(login) ? null : login.Trim();

    private static string? NormalizePassword(string? password) =>
        string.IsNullOrEmpty(password) ? null : password;

    private async Task<bool> AnotherActiveAdminWithLoginExistsAsync(string userId) =>
        await _userManager.Users.AnyAsync(u => u.Id != userId
            && u.Role == Core.Enums.UserRole.Admin
            && u.IsActive
            && u.UserName != null);

    private async Task<IdentityResult> ValidatePasswordAsync(User user, string password)
    {
        foreach (var validator in _userManager.PasswordValidators)
        {
            var result = await validator.ValidateAsync(_userManager, user, password);
            if (!result.Succeeded)
                return result;
        }
        return IdentityResult.Success;
    }
}