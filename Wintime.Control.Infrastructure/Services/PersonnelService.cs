using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.DTOs.Personnel;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;

namespace Wintime.Control.Infrastructure.Services;

public sealed class PersonnelService : IPersonnelService
{
    private readonly ControlDbContext _context;
    private readonly UserManager<User> _userManager;
    private readonly IRefreshTokenStore _refreshTokenStore;

    public PersonnelService(
        ControlDbContext context,
        UserManager<User> userManager,
        IRefreshTokenStore refreshTokenStore)
    {
        _context = context;
        _userManager = userManager;
        _refreshTokenStore = refreshTokenStore;
    }

    public async Task<PersonnelUpdateResult> UpdateAsync(
        string id,
        UpdatePersonnelRequestDto request,
        bool callerIsAdmin,
        CancellationToken ct)
    {
        var initialUser = await FindUserAsync(id, ct);
        if (initialUser == null)
            return new PersonnelUpdateResult(PersonnelUpdateStatus.NotFound);

        var initialAccessError = ValidateRoleAccess(
            initialUser, request, callerIsAdmin, out var requestedRole);
        if (initialAccessError != null)
            return initialAccessError;

        var requiresScheduleLock = initialUser.Role == UserRole.Manager
            || requestedRole == UserRole.Manager
            || (request.IsActive.HasValue && request.IsActive.Value != initialUser.IsActive);

        var strategy = _context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(ct);
            if (requiresScheduleLock)
                await ShiftScheduleLock.AcquireAsync(_context, ct);

            // FindByIdAsync может вернуть уже отслеживаемый экземпляр. Свежий no-tracking
            // запрос после advisory lock повторяет все guards по зафиксированному состоянию.
            var user = requiresScheduleLock ? await FindUserAsync(id, ct) : initialUser;
            if (user == null)
                return new PersonnelUpdateResult(PersonnelUpdateStatus.NotFound);

            var outcome = await ApplyUpdateAsync(user, request, callerIsAdmin, ct);
            if (outcome.Result.Status != PersonnelUpdateStatus.NoContent)
                return outcome.Result;

            await transaction.CommitAsync(ct);
            if (outcome.RevokeRefreshTokens)
                _refreshTokenStore.RevokeAllForUser(user.Id);
            return outcome.Result;
        });
    }

    private async Task<UpdateOutcome> ApplyUpdateAsync(
        User user,
        UpdatePersonnelRequestDto request,
        bool callerIsAdmin,
        CancellationToken ct)
    {
        var accessError = ValidateRoleAccess(user, request, callerIsAdmin, out var newRole);
        if (accessError != null)
            return new UpdateOutcome(accessError, false);

        var loginSpecified = request.Login != null;
        var originalLogin = user.UserName;
        var targetLogin = loginSpecified ? NormalizeLogin(request.Login) : originalLogin;
        var password = NormalizePassword(request.Password);

        if (targetLogin == null && password != null)
            return BadRequest("Пароль нельзя задать без логина");
        if (targetLogin != null && string.IsNullOrEmpty(user.PasswordHash) && password == null)
            return BadRequest("При включении входа пароль обязателен");

        var removesAdminAccess = user.Role == UserRole.Admin
            && user.IsActive
            && !string.IsNullOrWhiteSpace(originalLogin)
            && ((loginSpecified && targetLogin == null)
                || request.IsActive == false
                || (newRole.HasValue && newRole.Value != UserRole.Admin));
        if (removesAdminAccess && !await AnotherActiveAdminWithLoginExistsAsync(user.Id, ct))
            return BadRequest(
                "Нельзя отозвать вход, деактивировать или понизить роль последнего активного администратора с доступом");

        if (password != null)
        {
            var passwordResult = await ValidatePasswordAsync(user, password);
            if (!passwordResult.Succeeded)
                return new UpdateOutcome(
                    new PersonnelUpdateResult(
                        PersonnelUpdateStatus.BadRequest,
                        Errors: passwordResult.Errors.ToList()),
                    false);
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

        // UserValidator при проверке уникальности логина загружает того же пользователя
        // tracking-запросом; присоединяем выбранный после lock экземпляр заранее.
        _context.Attach(user);
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
            return new UpdateOutcome(
                new PersonnelUpdateResult(
                    PersonnelUpdateStatus.BadRequest,
                    Errors: result.Errors.ToList()),
                false);

        return new UpdateOutcome(
            new PersonnelUpdateResult(PersonnelUpdateStatus.NoContent),
            loginSpecified && targetLogin == null);
    }

    private static PersonnelUpdateResult? ValidateRoleAccess(
        User user,
        UpdatePersonnelRequestDto request,
        bool callerIsAdmin,
        out UserRole? newRole)
    {
        newRole = null;

        // Руководитель управляет персоналом, но не администраторами: не может ни менять
        // учётку админа, ни выдать роль Admin (иначе повысил бы себя или другого).
        if (!callerIsAdmin && user.Role == UserRole.Admin)
            return new PersonnelUpdateResult(PersonnelUpdateStatus.Forbid);

        if (request.Role != null)
        {
            if (!Enum.TryParse<UserRole>(request.Role, out var parsedRole))
                return BadRequestResult("Неверная роль");
            if (parsedRole == UserRole.Admin && !callerIsAdmin)
                return new PersonnelUpdateResult(PersonnelUpdateStatus.Forbid);
            newRole = parsedRole;
        }

        return null;
    }

    private async Task<User?> FindUserAsync(string id, CancellationToken ct) =>
        await _context.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, ct);

    private static PersonnelUpdateResult BadRequestResult(string message) =>
        new(PersonnelUpdateStatus.BadRequest, message);

    private static UpdateOutcome BadRequest(string message) =>
        new(BadRequestResult(message), false);

    private static string? NormalizeLogin(string? login) =>
        string.IsNullOrWhiteSpace(login) ? null : login.Trim();

    private static string? NormalizePassword(string? password) =>
        string.IsNullOrEmpty(password) ? null : password;

    private async Task<bool> AnotherActiveAdminWithLoginExistsAsync(string userId, CancellationToken ct) =>
        await _context.Users.AnyAsync(u => u.Id != userId
            && u.Role == UserRole.Admin
            && u.IsActive
            && u.UserName != null, ct);

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

    private sealed record UpdateOutcome(PersonnelUpdateResult Result, bool RevokeRefreshTokens);
}
