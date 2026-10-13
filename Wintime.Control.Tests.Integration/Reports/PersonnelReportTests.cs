using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Report;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Reports;

/// <summary>ARCH-03: наладки и задания наладчика — по интервалам задания, не по событиям/сменам.</summary>
[Collection("Integration")]
public class PersonnelReportTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public PersonnelReportTests(IntegrationTestFactory factory) => _factory = factory;

    // Отчётные сутки; метки времени в полдень UTC — те же локальные сутки при поясе UTC−11…UTC+11
    private static readonly DateTime Day = new(2025, 5, 12, 0, 0, 0, DateTimeKind.Utc);

    private async Task<string> CreateAdjusterAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var um = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var name = $"adj_{Guid.NewGuid():N}";
        var user = new User { UserName = name, Email = $"{name}@test.local", FullName = name, Role = UserRole.Adjuster, IsActive = true };
        (await um.CreateAsync(user, "Passw0rd!")).Succeeded.Should().BeTrue();
        return user.Id;
    }

    private async Task SeedAsync(string adjusterId, Guid immId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var mold = new Mold { Name = $"M-{Guid.NewGuid():N}", FormId = $"F-{Guid.NewGuid():N}", Cavities = 1, IsActive = true };
        db.Molds.Add(mold);
        await db.SaveChangesAsync();

        ShiftTask T(TaskStatus st, DateTime? setup, DateTime? started, DateTime? completed) => new()
        {
            ImmId = immId, MoldId = mold.Id, PersonnelId = adjusterId, PlanQuantity = 10, Status = st,
            PlannedDate = DateOnly.FromDateTime(Day), IssuedAt = Day.AddDays(-2),
            SetupStartedAt = setup, StartedAt = started, CompletedAt = completed
        };

        db.ShiftTasks.AddRange(
            // Наладка через границу суток: накануне 12:00 → сегодня 12:00 (24 ч) — относится к сегодня;
            // задание ещё в работе
            T(TaskStatus.InProgress, Day.AddHours(-12), Day.AddHours(12), null),
            // Начато позавчера, завершено сегодня — «выполнено заданий» +1, наладка не сегодня
            T(TaskStatus.Completed, Day.AddDays(-2).AddHours(10), Day.AddDays(-2).AddHours(11), Day.AddHours(13)),
            // Наладка сегодня 1 ч, задание в работе
            T(TaskStatus.InProgress, Day.AddHours(10), Day.AddHours(11), null),
            // Идёт наладка — не завершена, не считается
            T(TaskStatus.Setup, Day.AddHours(14), null, null),
            // Отменённая наладка (CancelSetup сбрасывает SetupStartedAt) — не считается
            T(TaskStatus.Issued, null, null, null));
        await db.SaveChangesAsync();
    }

    private async Task<AssetsReportDto> GetAsync(string reportType)
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        var resp = await client.GetAsync($"/api/reports/assets?dateFrom={Day:yyyy-MM-dd}&dateTo={Day:yyyy-MM-dd}&reportType={reportType}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await resp.Content.ReadFromJsonAsync<AssetsReportDto>())!;
    }

    [Fact]
    public async Task Personnel_CountsSetupsByCompletion_TasksByCompletedAt()
    {
        var adjusterId = await CreateAdjusterAsync();
        var immId = await _factory.CreateFreshImmAsync();
        await SeedAsync(adjusterId, immId);

        var report = await GetAsync("Personnel");
        var row = report.PersonnelData.Should().ContainSingle(p => p.PersonnelId == adjusterId).Subject;

        row.CompletedTasks.Should().Be(1);
        row.CompletedSetups.Should().Be(2);
        row.TotalSetupSeconds.Should().Be(24 * 3600 + 3600);
        row.AvgSetupTime.Should().Be((24 * 3600 + 3600) / 2m);
    }

    [Fact]
    public async Task PersonnelByImm_SameRulesWithImmBreakdown()
    {
        var adjusterId = await CreateAdjusterAsync();
        var immId = await _factory.CreateFreshImmAsync();
        await SeedAsync(adjusterId, immId);

        var report = await GetAsync("PersonnelByImm");
        var row = report.PersonnelByImmData.Should().ContainSingle(p => p.PersonnelId == adjusterId).Subject;

        row.CompletedTasks.Should().Be(1);
        row.CompletedSetups.Should().Be(2);
        row.TotalSetupSeconds.Should().Be(24 * 3600 + 3600);
        var imm = row.ImmBreakdown.Should().ContainSingle().Subject;
        imm.ImmId.Should().Be(immId);
        imm.CompletedSetups.Should().Be(2);
        imm.CompletedTasks.Should().Be(1);
    }
}
