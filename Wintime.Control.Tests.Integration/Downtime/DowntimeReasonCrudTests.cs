using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Downtime;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;

namespace Wintime.Control.Tests.Integration.Downtime;

/// <summary>
/// Редактирование и удаление причин простоя в справочнике.
/// Доступно только менеджерскому уровню (Admin/Manager).
/// Редактирование: наименование обязательно и не должно дублировать уже имеющееся.
/// Удаление: только если причина не использовалась в журнале простоев.
/// </summary>
[Collection("Integration")]
public class DowntimeReasonCrudTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public DowntimeReasonCrudTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task<Guid> SeedReasonAsync(string name, string type = "Planned")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var reason = new DowntimeReason { Name = name, Type = type, IsActive = true };
        db.DowntimeReasons.Add(reason);
        await db.SaveChangesAsync();
        return reason.Id;
    }

    // ---------- Update ----------

    [Fact]
    public async Task Manager_CanUpdateReason()
    {
        var id = await SeedReasonAsync($"Старое {Guid.NewGuid():N}");
        var client = await ManagerClientAsync();

        var body = new UpdateDowntimeReasonRequestDto
        {
            Name = $"Новое {Guid.NewGuid():N}",
            Type = "Emergency",
            IsActive = false
        };

        var resp = await client.PutAsJsonAsync($"/api/downtime/reasons/{id}", body);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var reason = await db.DowntimeReasons.FindAsync(id);
        reason!.Name.Should().Be(body.Name);
        reason.Type.Should().Be("Emergency");
        reason.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Update_EmptyName_ReturnsBadRequest()
    {
        var id = await SeedReasonAsync($"Причина {Guid.NewGuid():N}");
        var client = await ManagerClientAsync();

        var body = new UpdateDowntimeReasonRequestDto { Name = "   ", Type = "Planned" };

        var resp = await client.PutAsJsonAsync($"/api/downtime/reasons/{id}", body);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_DuplicateName_ReturnsConflict()
    {
        var existingName = $"Дубликат {Guid.NewGuid():N}";
        await SeedReasonAsync(existingName);
        var id = await SeedReasonAsync($"Другая {Guid.NewGuid():N}");
        var client = await ManagerClientAsync();

        // Пытаемся переименовать вторую причину в имя первой (без учёта регистра/пробелов).
        var body = new UpdateDowntimeReasonRequestDto { Name = $"  {existingName.ToUpper()}  ", Type = "Planned" };

        var resp = await client.PutAsJsonAsync($"/api/downtime/reasons/{id}", body);

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_SameNameAsItself_Succeeds()
    {
        var name = $"Без изменений {Guid.NewGuid():N}";
        var id = await SeedReasonAsync(name);
        var client = await ManagerClientAsync();

        var body = new UpdateDowntimeReasonRequestDto { Name = name, Type = "Emergency", IsActive = true };

        var resp = await client.PutAsJsonAsync($"/api/downtime/reasons/{id}", body);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Update_UnknownReason_ReturnsNotFound()
    {
        var client = await ManagerClientAsync();
        var body = new UpdateDowntimeReasonRequestDto { Name = "X", Type = "Planned" };

        var resp = await client.PutAsJsonAsync($"/api/downtime/reasons/{Guid.NewGuid()}", body);

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Adjuster_CannotUpdate_ReturnsForbidden()
    {
        var id = await SeedReasonAsync($"Причина {Guid.NewGuid():N}");
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_adjuster", "Adjuster123!");
        AuthHelper.SetBearerToken(client, token!);

        var body = new UpdateDowntimeReasonRequestDto { Name = "Хак", Type = "Planned" };

        var resp = await client.PutAsJsonAsync($"/api/downtime/reasons/{id}", body);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---------- Delete ----------

    [Fact]
    public async Task Manager_CanDeleteUnusedReason()
    {
        var id = await SeedReasonAsync($"Неиспользуемая {Guid.NewGuid():N}");
        var client = await ManagerClientAsync();

        var resp = await client.DeleteAsync($"/api/downtime/reasons/{id}");

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        (await db.DowntimeReasons.FindAsync(id)).Should().BeNull();
    }

    [Fact]
    public async Task Delete_UsedReason_ReturnsConflict()
    {
        var id = await SeedReasonAsync($"Используемая {Guid.NewGuid():N}");

        // Привязываем причину к событию в журнале простоев.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            var imm = new Core.Entities.Imm
            {
                Name = $"IMM-{Guid.NewGuid():N}",
                TemplateId = _factory.TestTemplateId,
                IsActive = true
            };
            db.Imms.Add(imm);
            db.Events.Add(new Event
            {
                ImmId = imm.Id,
                EventType = EventType.Downtime,
                ReasonId = id,
                StartTime = DateTime.UtcNow.AddMinutes(-10),
                EndTime = DateTime.UtcNow,
                IsAuto = false
            });
            await db.SaveChangesAsync();
        }

        var client = await ManagerClientAsync();

        var resp = await client.DeleteAsync($"/api/downtime/reasons/{id}");

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ControlDbContext>();
        (await verifyDb.DowntimeReasons.FindAsync(id)).Should().NotBeNull();
    }

    [Fact]
    public async Task Delete_UnknownReason_ReturnsNotFound()
    {
        var client = await ManagerClientAsync();

        var resp = await client.DeleteAsync($"/api/downtime/reasons/{Guid.NewGuid()}");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Adjuster_CannotDelete_ReturnsForbidden()
    {
        var id = await SeedReasonAsync($"Причина {Guid.NewGuid():N}");
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_adjuster", "Adjuster123!");
        AuthHelper.SetBearerToken(client, token!);

        var resp = await client.DeleteAsync($"/api/downtime/reasons/{id}");

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
