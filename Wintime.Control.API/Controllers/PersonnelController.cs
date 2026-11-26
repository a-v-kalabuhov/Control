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
    private readonly IPersonnelService _personnelService;

    public PersonnelController(UserManager<User> userManager, IPersonnelService personnelService)
    {
        _userManager = userManager;
        _personnelService = personnelService;
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
        if (!Enum.TryParse<Core.Enums.UserRole>(request.Role, out var role) ||
            !Enum.IsDefined(role))
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
    public async Task<IActionResult> UpdatePersonnel(
        string id, [FromBody] UpdatePersonnelRequestDto request, CancellationToken ct)
    {
        var result = await _personnelService.UpdateAsync(
            id, request, User.IsInRole(Roles.Admin), ct);
        return result.Status switch
        {
            PersonnelUpdateStatus.NoContent => NoContent(),
            PersonnelUpdateStatus.NotFound => NotFound(),
            PersonnelUpdateStatus.Forbid => Forbid(),
            PersonnelUpdateStatus.BadRequest when result.Errors != null => BadRequest(result.Errors),
            PersonnelUpdateStatus.BadRequest => BadRequest(result.Message),
            _ => throw new InvalidOperationException("Неизвестный результат обновления персонала")
        };
    }

    private static string? NormalizeLogin(string? login) =>
        string.IsNullOrWhiteSpace(login) ? null : login.Trim();

    private static string? NormalizePassword(string? password) =>
        string.IsNullOrEmpty(password) ? null : password;
}