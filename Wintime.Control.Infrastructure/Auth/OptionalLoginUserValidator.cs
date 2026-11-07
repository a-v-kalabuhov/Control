using Microsoft.AspNetCore.Identity;
using Wintime.Control.Core.Entities;

namespace Wintime.Control.Infrastructure.Auth;

/// <summary>
/// Разрешает справочные записи персонала без входа в систему. Для заполненного
/// логина сохраняются стандартные проверки Identity (символы и уникальность).
/// </summary>
public sealed class OptionalLoginUserValidator : UserValidator<User>
{
    public override async Task<IdentityResult> ValidateAsync(UserManager<User> manager, User user)
    {
        if (string.IsNullOrWhiteSpace(user.UserName))
        {
            user.UserName = null;
            user.NormalizedUserName = null;
            return IdentityResult.Success;
        }

        user.UserName = user.UserName.Trim();
        return await base.ValidateAsync(manager, user);
    }
}
