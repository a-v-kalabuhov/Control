using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Personnel;
using Wintime.Control.Core.DTOs.Shifts;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Auth;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Auth;

/// <summary>
/// Управление персоналом и расписанием смен: доступно Admin и Manager.
/// Manager не может выдать роль Admin и не может менять учётку администратора —
/// иначе руководитель повысил бы себя или другого до администратора.
/// </summary>
[Collection("Integration")]
public class PersonnelManagementTests : IClassFixture<IntegrationTestFactory>
{
    private const string AdminPassword = "Admin123!";

    private readonly IntegrationTestFactory _factory;

    public PersonnelManagementTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ClientAsync(string login, string password)
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, login, password);
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private Task<HttpClient> ManagerClientAsync() => ClientAsync("test_manager", "Manager123!");

    private async Task<string> SeedUserAsync(UserRole role, string password = AdminPassword)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var login = $"u_{Guid.NewGuid():N}";
        var user = new User
        {
            UserName = login,
            Email = $"{login}@control.local",
            FullName = "Seeded User",
            Role = role,
            IsActive = true
        };
        var result = await userManager.CreateAsync(user, password);
        result.Succeeded.Should().BeTrue();
        return user.Id;
    }

    private async Task<User> LoadUserAsync(string id)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        return (await userManager.FindByIdAsync(id))!;
    }

    private async Task<string> SeedUserWithoutLoginAsync(UserRole role)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = new User
        {
            UserName = null,
            Email = null,
            FullName = "Сотрудник без входа",
            Role = role,
            IsActive = true
        };
        var result = await userManager.CreateAsync(user);
        result.Succeeded.Should().BeTrue(string.Join("; ", result.Errors.Select(e => e.Description)));
        return user.Id;
    }

    private static CreatePersonnelRequestDto NewPerson(string role) => new()
    {
        EmployeeId = "EMP-T",
        FullName = "Тестовый Сотрудник",
        Login = $"p_{Guid.NewGuid():N}",
        Password = "Passw0rd!",
        Role = role
    };

    // ---------- Create ----------

    [Fact]
    public async Task Manager_CanCreateAdjuster()
    {
        var client = await ManagerClientAsync();

        var resp = await client.PostAsJsonAsync("/api/personnel", NewPerson("Adjuster"));

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Manager_CannotCreateAdmin()
    {
        var client = await ManagerClientAsync();

        var resp = await client.PostAsJsonAsync("/api/personnel", NewPerson("Admin"));

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Manager_CanCreateShiftLead_WithoutGrantingExistingOperationalPermissions()
    {
        var manager = await ManagerClientAsync();
        var request = NewPerson("ShiftLead");

        var createdResponse = await manager.PostAsJsonAsync("/api/personnel", request);

        createdResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createdResponse.Content.ReadFromJsonAsync<PersonnelDto>();
        created.Should().NotBeNull();
        created!.IsActive.Should().BeTrue();

        var getResponse = await manager.GetAsync($"/api/personnel/{created.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await getResponse.Content.ReadFromJsonAsync<PersonnelDto>();
        saved!.Role.Should().Be("ShiftLead");

        var shiftLead = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(shiftLead, request.Login!, request.Password!);
        token.Should().NotBeNull();
        AuthHelper.SetBearerToken(shiftLead, token!);

        foreach (var url in new[] { "/api/tasks", "/api/personnel", "/api/imm" })
        {
            var response = await shiftLead.GetAsync(url);
            response.StatusCode.Should().Be(HttpStatusCode.Forbidden, url);
        }
    }

    [Fact]
    public async Task Admin_CanCreateAdmin()
    {
        var adminId = await SeedUserAsync(UserRole.Admin);
        var admin = await LoadUserAsync(adminId);
        var client = await ClientAsync(admin.UserName!, AdminPassword);

        var resp = await client.PostAsJsonAsync("/api/personnel", NewPerson("Admin"));

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Adjuster_CannotCreatePersonnel()
    {
        var client = await ClientAsync("test_adjuster", "Adjuster123!");

        var resp = await client.PostAsJsonAsync("/api/personnel", NewPerson("Adjuster"));

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---------- Update ----------

    [Fact]
    public async Task Manager_CanDeactivateAdjuster()
    {
        var id = await SeedUserAsync(UserRole.Adjuster);
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync($"/api/personnel/{id}",
            new UpdatePersonnelRequestDto { IsActive = false });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await LoadUserAsync(id)).IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Manager_CannotPromoteToAdmin()
    {
        var id = await SeedUserAsync(UserRole.Adjuster);
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync($"/api/personnel/{id}",
            new UpdatePersonnelRequestDto { Role = "Admin" });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await LoadUserAsync(id)).Role.Should().Be(UserRole.Adjuster);
    }

    [Fact]
    public async Task Manager_CannotModifyAdmin()
    {
        var id = await SeedUserAsync(UserRole.Admin);
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync($"/api/personnel/{id}",
            new UpdatePersonnelRequestDto { IsActive = false });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await LoadUserAsync(id)).IsActive.Should().BeTrue();
    }

    // ---------- PZP-30: необязательный логин ----------

    [Fact]
    public async Task Manager_CanCreateMultipleOperatorsWithoutLogin()
    {
        var client = await ManagerClientAsync();

        foreach (var i in new[] { 1, 2 })
        {
            var request = NewPerson("Operator");
            request.Login = null;
            request.Password = null;
            request.EmployeeId = $"EMP-NO-LOGIN-{i}";

            var resp = await client.PostAsJsonAsync("/api/personnel", request);

            resp.StatusCode.Should().Be(HttpStatusCode.Created);
            var dto = await resp.Content.ReadFromJsonAsync<PersonnelDto>();
            var saved = await LoadUserAsync(dto!.Id);
            saved.UserName.Should().BeNull();
            saved.NormalizedUserName.Should().BeNull();
            saved.PasswordHash.Should().BeNull();
            saved.Role.Should().Be(UserRole.Operator);
        }
    }

    [Fact]
    public async Task Manager_CannotCreateLoginWithoutPassword()
    {
        var client = await ManagerClientAsync();
        var request = NewPerson("Operator");
        request.Password = null;

        var resp = await client.PostAsJsonAsync("/api/personnel", request);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Manager_CanGrantAndRevokeAccess()
    {
        var id = await SeedUserWithoutLoginAsync(UserRole.Operator);
        var client = await ManagerClientAsync();
        var login = $"operator_{Guid.NewGuid():N}";

        var grant = await client.PutAsJsonAsync($"/api/personnel/{id}",
            new UpdatePersonnelRequestDto { Login = login, Password = "Passw0rd!" });

        grant.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var granted = await LoadUserAsync(id);
        granted.UserName.Should().Be(login);
        granted.PasswordHash.Should().NotBeNull();

        string refreshToken;
        using (var scope = _factory.Services.CreateScope())
        {
            var tokens = scope.ServiceProvider.GetRequiredService<IRefreshTokenStore>();
            refreshToken = $"refresh-{Guid.NewGuid():N}";
            tokens.Store(refreshToken, id, DateTime.UtcNow.AddDays(1));
        }

        var revoke = await client.PutAsJsonAsync($"/api/personnel/{id}",
            new UpdatePersonnelRequestDto { Login = "" });

        revoke.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var revoked = await LoadUserAsync(id);
        revoked.UserName.Should().BeNull();
        revoked.NormalizedUserName.Should().BeNull();
        revoked.PasswordHash.Should().BeNull();
        using (var scope = _factory.Services.CreateScope())
        {
            var tokens = scope.ServiceProvider.GetRequiredService<IRefreshTokenStore>();
            tokens.GetActive(refreshToken).Should().BeNull();
        }
    }

    [Fact]
    public async Task Refresh_RejectsActiveUserWithoutLogin()
    {
        var id = await SeedUserWithoutLoginAsync(UserRole.Operator);
        string accessToken;
        string refreshToken;
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = await userManager.FindByIdAsync(id);
            accessToken = scope.ServiceProvider.GetRequiredService<IJwtTokenService>().GenerateAccessToken(user!);
            refreshToken = $"refresh-{Guid.NewGuid():N}";
            scope.ServiceProvider.GetRequiredService<IRefreshTokenStore>()
                .Store(refreshToken, id, DateTime.UtcNow.AddDays(1));
        }
        var client = _factory.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/auth/refresh", new { accessToken, refreshToken });

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("deactivate")]
    [InlineData("demote")]
    public async Task Admin_CannotRemoveAccessOfLastActiveAdmin(string mode)
    {
        var id = await SeedUserAsync(UserRole.Admin);
        var admin = await LoadUserAsync(id);
        var client = await ClientAsync(admin.UserName!, AdminPassword);
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var otherAdmins = await userManager.Users
                .Where(u => u.Role == UserRole.Admin && u.Id != id && u.IsActive)
                .ToListAsync();
            foreach (var other in otherAdmins)
            {
                other.IsActive = false;
                await userManager.UpdateAsync(other);
            }
        }

        var request = mode switch
        {
            "deactivate" => new UpdatePersonnelRequestDto { IsActive = false },
            "demote" => new UpdatePersonnelRequestDto { Role = "Manager" },
            _ => new UpdatePersonnelRequestDto { Login = "" }
        };
        var resp = await client.PutAsJsonAsync($"/api/personnel/{id}", request);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var saved = await LoadUserAsync(id);
        saved.IsActive.Should().BeTrue();
        saved.UserName.Should().Be(admin.UserName);
        saved.Role.Should().Be(UserRole.Admin);
    }

    // ---------- Shifts ----------

    [Fact]
    public async Task Manager_IsAllowedToSaveShifts()
    {
        // Пустой список — валидационная ошибка: проверяем, что авторизация пропустила
        // менеджера (400, а не 403), не меняя общее расписание смен тестовой БД.
        var client = await ManagerClientAsync();

        var resp = await client.PutAsJsonAsync("/api/shifts",
            new SaveShiftsRequestDto { Shifts = new() });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Adjuster_CannotSaveShifts()
    {
        var client = await ClientAsync("test_adjuster", "Adjuster123!");

        var resp = await client.PutAsJsonAsync("/api/shifts",
            new SaveShiftsRequestDto { Shifts = new() });

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
