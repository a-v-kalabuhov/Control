using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Personnel;
using Wintime.Control.Core.DTOs.Shifts;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
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
