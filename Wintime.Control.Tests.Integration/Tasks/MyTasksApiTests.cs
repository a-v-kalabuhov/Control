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

[Collection("Integration")]
public class MyTasksApiTests : IClassFixture<IntegrationTestFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly DateOnly Tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
    private readonly IntegrationTestFactory _factory;
    public MyTasksApiTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<HttpClient> AdjusterClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_adjuster", "Adjuster123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    // Каждый тест — на своём ТПА; search по имени ТПА изолирует тесты в общей БД класса.
    private async Task<(Guid immId, string search)> FreshImmAsync()
    {
        var immId = await _factory.CreateFreshImmAsync();
        return (immId, immId.ToString("N"));
    }

    private async Task<Guid> SeedAsync(Guid immId, TaskStatus status, DateOnly date, TimeOnly? time = null,
                                       DateTime? completedAt = null, string? personnelId = "self")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var task = new ShiftTask
        {
            ImmId = immId, MoldId = _factory.TestMoldId, PlanQuantity = 10, Status = status,
            PersonnelId = personnelId == "self" ? _factory.TestAdjusterId : personnelId,
            PlannedDate = date, PlannedSetupStartTime = time, PlannedFullCycleSeconds = 30,
            IssuedAt = status == TaskStatus.Planned ? null : DateTime.UtcNow.AddDays(-20),
            CompletedAt = completedAt
        };
        db.ShiftTasks.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }

    private async Task<JsonElement> GetMyAsync(string search) =>
        await (await AdjusterClientAsync())
            .GetFromJsonAsync<JsonElement>($"/api/tasks/my?search={search}", JsonOptions);

    private static List<string> Ids(JsonElement arr) =>
        arr.EnumerateArray().Select(e => e.GetProperty("id").GetString()!).ToList();

    [Fact]
    public async Task GetMyTasks_QueueSortedByPlannedStart()
    {
        var (imm, search) = await FreshImmAsync();
        var late  = await SeedAsync(imm, TaskStatus.Issued, Tomorrow.AddDays(5));
        var early = await SeedAsync(imm, TaskStatus.Issued, Tomorrow, new TimeOnly(6, 0));
        var mid   = await SeedAsync(imm, TaskStatus.Issued, Tomorrow, new TimeOnly(15, 0));

        var my = await GetMyAsync(search);

        Ids(my.GetProperty("queue")).Should().Equal(early.ToString(), mid.ToString(), late.ToString());
    }

    [Fact]
    public async Task GetMyTasks_ActiveSectionHasSetupAndInProgress()
    {
        var (imm, search) = await FreshImmAsync();
        var setup = await SeedAsync(imm, TaskStatus.Setup, Tomorrow);
        var work  = await SeedAsync(imm, TaskStatus.InProgress, Tomorrow);
        await SeedAsync(imm, TaskStatus.Issued, Tomorrow);

        var my = await GetMyAsync(search);

        Ids(my.GetProperty("active")).Should().BeEquivalentTo(new[] { setup.ToString(), work.ToString() });
        my.GetProperty("queue").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task GetMyTasks_ArchivePagedNewestFirst()
    {
        var (imm, search) = await FreshImmAsync();
        var older = await SeedAsync(imm, TaskStatus.Completed, Tomorrow.AddDays(-10), completedAt: DateTime.UtcNow.AddDays(-3));
        var newer = await SeedAsync(imm, TaskStatus.Completed, Tomorrow.AddDays(-10), completedAt: DateTime.UtcNow.AddDays(-1));

        var my = await GetMyAsync(search);

        var archive = my.GetProperty("archive");
        archive.GetProperty("total").GetInt32().Should().Be(2);
        Ids(archive.GetProperty("items")).Should().Equal(newer.ToString(), older.ToString());
    }

    [Fact]
    public async Task GetMyTasks_PlannedHidden()
    {
        var (imm, search) = await FreshImmAsync();
        await SeedAsync(imm, TaskStatus.Planned, Tomorrow);

        var my = await GetMyAsync(search);

        my.GetProperty("active").GetArrayLength().Should().Be(0);
        my.GetProperty("queue").GetArrayLength().Should().Be(0);
        my.GetProperty("archive").GetProperty("total").GetInt32().Should().Be(0);
    }
}
