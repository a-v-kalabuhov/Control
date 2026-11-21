using Microsoft.AspNetCore.Identity;

namespace Wintime.Control.Infrastructure.Auth;

/// <summary>
/// Русские сообщения об ошибках проверки пароля ASP.NET Core Identity.
/// </summary>
public sealed class RussianIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError PasswordTooShort(int length) => new()
    {
        Code = nameof(PasswordTooShort),
        Description = $"Пароль должен содержать не менее {length} символов."
    };

    public override IdentityError PasswordRequiresNonAlphanumeric() => new()
    {
        Code = nameof(PasswordRequiresNonAlphanumeric),
        Description = "Пароль должен содержать хотя бы один специальный символ."
    };

    public override IdentityError PasswordRequiresDigit() => new()
    {
        Code = nameof(PasswordRequiresDigit),
        Description = "Пароль должен содержать хотя бы одну цифру ('0'-'9')."
    };

    public override IdentityError PasswordRequiresLower() => new()
    {
        Code = nameof(PasswordRequiresLower),
        Description = "Пароль должен содержать хотя бы одну строчную букву ('a'-'z')."
    };

    public override IdentityError PasswordRequiresUpper() => new()
    {
        Code = nameof(PasswordRequiresUpper),
        Description = "Пароль должен содержать хотя бы одну прописную букву ('A'-'Z')."
    };
}
