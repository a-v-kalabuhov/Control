using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Tasks;

/// <summary>PZP-07b: старт наладки с ПФ, занятой на другом ТПА, — предупреждение с подтверждением, не запрет.</summary>
[Collection("Integration")]
public class StartSetupMoldBusyTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public StartSetupMoldBusyTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> AdjusterAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_adjuster", "Adjuster123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task<Guid> SeedMoldAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var mold = new Mold
        {
            Name = "M-busy", FormId = $"FORM-{Guid.NewGuid():N}", Cavities = 2, IsActive = true,
            ProductTypeId = _factory.TestProductTypeId
        };
        db.Molds.Add(mold);
        await db.SaveChangesAsync();
        return mold.Id;
    }

    private async Task<Guid> SeedTaskAsync(Guid immId, Guid moldId, TaskStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var task = new ShiftTask
        {
            ImmId = immId, MoldId = moldId, ProductId = _factory.TestProductId, PlanQuantity = 100,
            PlannedFullCycleSeconds = 60, Status = status, PersonnelId = _factory.TestAdjusterId,
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow),
            SetupStartedAt = status == TaskStatus.Issued ? null : DateTime.UtcNow.AddMinutes(-30)
        };
        db.ShiftTasks.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }

    [Theory]
    [InlineData(TaskStatus.Setup)]
    [InlineData(TaskStatus.InProgress)]
    public async Task Start_with_mold_busy_on_other_imm_warns_then_starts_with_confirmation(TaskStatus busyStatus)
    {
        var mold = await SeedMoldAsync();
        var busyImm = await _factory.CreateFreshImmAsync();
        await SeedTaskAsync(busyImm, mold, busyStatus);
        var taskId = await SeedTaskAsync(await _factory.CreateFreshImmAsync(), mold, TaskStatus.Issued);
        var client = await AdjusterAsync();

        var warned = await client.PostAsync($"/api/tasks/{taskId}/start", null);
        warned.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await warned.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("message").GetString().Should().Contain("Пресс-форма сейчас используется");
        body.GetProperty("moldBusy").GetProperty("immName").GetString().Should().Be($"ТПА-{busyImm:N}");

        var started = await client.PostAsJsonAsync($"/api/tasks/{taskId}/start", new { confirmMoldBusy = true });
        started.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Start_with_mold_free_needs_no_confirmation()
    {
        var mold = await SeedMoldAsync();
        await SeedTaskAsync(await _factory.CreateFreshImmAsync(), mold, TaskStatus.Planned);
        var taskId = await SeedTaskAsync(await _factory.CreateFreshImmAsync(), mold, TaskStatus.Issued);
        var client = await AdjusterAsync();

        (await client.PostAsync($"/api/tasks/{taskId}/start", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
