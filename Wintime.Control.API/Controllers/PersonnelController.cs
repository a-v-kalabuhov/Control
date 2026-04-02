using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.Personnel;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Shared.Constants;

namespace Wintime.Control.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PersonnelController : ControllerBase
{
    private readonly UserManager<User> _userManager;

    public PersonnelController(UserManager<User> userManager)
    {
        _userManager = userManager;
    }

    /// <summary>
    /// Список персонала
    /// </summary>
    [HttpGet]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
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

        var dtos = users.Select(u => new PersonnelDto
        {
            Id = u.Id,
            EmployeeId = u.EmployeeId,
            FullName = u.FullName,
            Qualification = u.Qualification,
            Role = u.Role.ToString(),
            IsActive = u.IsActive
        }).ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// Создать запись персонала
    /// </summary>
    [HttpPost]
    [Authorize(Roles = $"{Roles.Admin}")]
    public async Task<ActionResult<PersonnelDto>> CreatePersonnel([FromBody] CreatePersonnelRequestDto request)
    {
        var user = new User
        {
            UserName = request.Login,
            Email = $"{request.Login}@control.local",
            FullName = request.FullName,
            EmployeeId = request.EmployeeId,
            Qualification = request.Qualification,
            IsActive = true
        };

        // Парсинг роли
        if (!Enum.TryParse<Core.Enums.UserRole>(request.Role, out var role))
            return BadRequest("Неверная роль");

        user.Role = role;

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return BadRequest(result.Errors);

        await _userManager.AddToRoleAsync(user, request.Role);

        var dto = new PersonnelDto
        {
            Id = user.Id,
            EmployeeId = user.EmployeeId,
            FullName = user.FullName,
            Qualification = user.Qualification,
            Role = user.Role.ToString(),
            IsActive = user.IsActive
        };

        return CreatedAtAction(nameof(GetPersonnelList), new { }, dto);
    }

    /// <summary>
    /// Обновить данные персонала
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = $"{Roles.Admin}")]
    public async Task<IActionResult> UpdatePersonnel(string id, [FromBody] UpdatePersonnelRequestDto request)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
            return NotFound();

        if (request.FullName != null)
            user.FullName = request.FullName;
        if (request.Qualification != null)
            user.Qualification = request.Qualification;
        if (request.IsActive.HasValue)
            user.IsActive = request.IsActive.Value;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
            return BadRequest(result.Errors);

        return NoContent();
    }
}