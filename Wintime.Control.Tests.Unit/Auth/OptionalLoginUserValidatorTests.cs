using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Auth;

namespace Wintime.Control.Tests.Unit.Auth;

public class OptionalLoginUserValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_AllowsUserWithoutLogin(string? login)
    {
        var validator = new OptionalLoginUserValidator();
        using var manager = CreateManager(validator);
        var user = new User { UserName = login, FullName = "Литейщик" };

        var result = await validator.ValidateAsync(manager, user);

        result.Succeeded.Should().BeTrue();
        user.UserName.Should().BeNull();
        user.NormalizedUserName.Should().BeNull();
    }

    [Fact]
    public async Task ValidateAsync_KeepsStandardValidationForProvidedLogin()
    {
        var validator = new OptionalLoginUserValidator();
        var store = Substitute.For<IUserStore<User>>();
        using var manager = CreateManager(validator, store);
        var user = new User { UserName = "operator", FullName = "Литейщик" };
        store.GetUserNameAsync(user, Arg.Any<CancellationToken>()).Returns(user.UserName);
        store.FindByNameAsync("OPERATOR", Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await validator.ValidateAsync(manager, user);

        result.Succeeded.Should().BeTrue();
        user.UserName.Should().Be("operator");
    }

    private static UserManager<User> CreateManager(
        OptionalLoginUserValidator validator,
        IUserStore<User>? store = null)
    {
        store ??= Substitute.For<IUserStore<User>>();
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        return new UserManager<User>(
            store,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<User>(),
            new[] { validator },
            new[] { new PasswordValidator<User>() },
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            services,
            services.GetRequiredService<ILogger<UserManager<User>>>());
    }
}
