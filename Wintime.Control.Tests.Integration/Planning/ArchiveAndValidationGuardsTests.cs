using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Planning;

/// <summary>PZP-07b: архив ТПА/ПФ только без незавершённых заданий; гнёзда ≥ 1; эталон цикла обязателен при правке.</summary>
[Collection("Integration")]
public class ArchiveAndValidationGuardsTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public ArchiveAndValidationGuardsTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> ManagerAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private async Task<Guid> SeedMoldAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var mold = new Mold
        {
            Name = "M-07b", FormId = $"FORM-{Guid.NewGuid():N}", Cavities = 2, IsActive = true,
            ProductTypeId = _factory.TestProductTypeId
        };
        db.Molds.Add(mold);
        await db.SaveChangesAsync();
        return mold.Id;
    }

    private async Task SeedTaskAsync(Guid immId, Guid moldId, TaskStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        db.ShiftTasks.Add(new ShiftTask
        {
            ImmId = immId, MoldId = moldId, ProductId = _factory.TestProductId, PlanQuantity = 100,
            PlannedFullCycleSeconds = 60, Status = status,
            PlannedDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3)
        });
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData(TaskStatus.Planned)]
    [InlineData(TaskStatus.Issued)]
    [InlineData(TaskStatus.Setup)]
    [InlineData(TaskStatus.InProgress)]
    public async Task Archiving_imm_with_open_task_is_rejected(TaskStatus status)
    {
        var immId = await _factory.CreateFreshImmAsync();
        await SeedTaskAsync(immId, await SeedMoldAsync(), status);
        var client = await ManagerAsync();

        var resp = await client.PutAsJsonAsync($"/api/imm/{immId}", new { isActive = false });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("незавершённые задания");
    }

    [Theory]
    [InlineData(TaskStatus.Completed)]
    [InlineData(TaskStatus.Closed)]
    public async Task Archiving_imm_with_only_finished_tasks_is_allowed(TaskStatus status)
    {
        var immId = await _factory.CreateFreshImmAsync();
        await SeedTaskAsync(immId, await SeedMoldAsync(), status);
        var client = await ManagerAsync();

        var resp = await client.PutAsJsonAsync($"/api/imm/{immId}", new { isActive = false });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Archiving_mold_with_open_task_is_rejected()
    {
        var moldId = await SeedMoldAsync();
        await SeedTaskAsync(await _factory.CreateFreshImmAsync(), moldId, TaskStatus.Planned);
        var client = await ManagerAsync();

        var resp = await client.PutAsJsonAsync($"/api/molds/{moldId}", new { isActive = false });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("незавершённые задания");
    }

    [Fact]
    public async Task Archiving_mold_with_only_finished_tasks_is_allowed()
    {
        var moldId = await SeedMoldAsync();
        await SeedTaskAsync(await _factory.CreateFreshImmAsync(), moldId, TaskStatus.Completed);
        var client = await ManagerAsync();

        var resp = await client.PutAsJsonAsync($"/api/molds/{moldId}", new { isActive = false });

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Creating_mold_with_zero_cavities_is_rejected()
    {
        var client = await ManagerAsync();

        var resp = await client.PostAsJsonAsync("/api/molds", new
        {
            formId = $"F-{Guid.NewGuid():N}", name = "Zero", cavities = 0, productTypeId = _factory.TestProductTypeId
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Updating_mold_to_zero_cavities_is_rejected()
    {
        var moldId = await SeedMoldAsync();
        var client = await ManagerAsync();

        var resp = await client.PutAsJsonAsync($"/api/molds/{moldId}", new { cavities = 0 });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
