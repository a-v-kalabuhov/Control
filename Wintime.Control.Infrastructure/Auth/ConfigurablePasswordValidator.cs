using Microsoft.AspNetCore.Identity;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Interfaces;

namespace Wintime.Control.Infrastructure.Auth;

/// <summary>
/// Делегирует проверку стандартному валидатору Identity, пока слабые пароли запрещены.
/// В режиме слабых паролей требует только непустое значение.
/// </summary>
public sealed class ConfigurablePasswordValidator : IPasswordValidator<User>
{
    private readonly ISecuritySettingsService _settings;
    private readonly IdentityErrorDescriber _errors;

    public ConfigurablePasswordValidator(ISecuritySettingsService settings, IdentityErrorDescriber errors)
    {
        _settings = settings;
        _errors = errors;
    }

    public async Task<IdentityResult> ValidateAsync(UserManager<User> manager, User user, string? password)
    {
        var settings = await _settings.GetAsync();
        if (!settings.AllowWeakPasswords)
            return await new PasswordValidator<User>(_errors).ValidateAsync(manager, user, password);

        return string.IsNullOrEmpty(password)
            ? IdentityResult.Failed(new IdentityError
            {
                Code = "PasswordRequired",
                Description = "Пароль не может быть пустым."
            })
            : IdentityResult.Success;
    }
}
