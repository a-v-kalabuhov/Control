using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.DTOs.Report;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Reports;

[Collection("Integration")]
public class EquipmentReportTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public EquipmentReportTests(IntegrationTestFactory factory) => _factory = factory;

    // Прошлые сутки, далеко от «сейчас» — без хвоста NoData от текущего момента.
    private static readonly DateTime Day = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-10), DateTimeKind.Utc);

    private async Task<Guid> SeedImmAsync(bool isActive, bool workingAllAround)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var imm = new Core.Entities.Imm
            { Name = $"IMM-{Guid.NewGuid():N}", TemplateId = _factory.TestTemplateId, IsActive = isActive };
        db.Imms.Add(imm);
        var mold = new Mold { Name = "M1", FormId = $"FORM-{Guid.NewGuid():N}", Cavities = 1, IsActive = true };
        db.Molds.Add(mold);
        await db.SaveChangesAsync();

        if (workingAllAround)
        {
            // Работа под заданием с запасом ±3 суток — покрывает сутки отчёта в любой зоне завода.
            var from = Day.AddDays(-3);
            var to   = Day.AddDays(4);
            db.ImmStatusHistory.Add(new ImmStatusHistory
                { ImmId = imm.Id, Status = ImmStatus.Auto, ChangedAt = from, EndedAt = to });
            db.ShiftTasks.Add(new ShiftTask
            {
                ImmId = imm.Id, MoldId = mold.Id, PlanQuantity = 100, Status = TaskStatus.Completed,
                SetupStartedAt = from, StartedAt = from, CompletedAt = to
            });
            await db.SaveChangesAsync();
        }
        return imm.Id;
    }

    private async Task<HttpClient> ManagerClientAsync()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        return client;
    }

    private static string Url(DateTime from, DateTime to, IEnumerable<Guid> ids, string? archive = null) =>
        $"/api/reports/equipment?dateFrom={from:yyyy-MM-dd}&dateTo={to:yyyy-MM-dd}"
        + string.Concat(ids.Select(id => $"&immIds={id}"))
        + (archive != null ? $"&archive={archive}" : "");

    [Fact]
    public async Task Returns_PerImm_DailyBreakdown_SummingToDayLength()
    {
        var working = await SeedImmAsync(isActive: true, workingAllAround: true);
        var silent  = await SeedImmAsync(isActive: true, workingAllAround: false);
        var client = await ManagerClientAsync();

        var resp = await client.GetAsync(Url(Day, Day.AddDays(1), new[] { working, silent }));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = (await resp.Content.ReadFromJsonAsync<EquipmentReportDto>())!;

        report.ImmData.Select(i => i.ImmId).Should().BeEquivalentTo(new[] { working, silent });

        var w = report.ImmData.Single(i => i.ImmId == working);
        w.Days.Should().HaveCount(2);
        foreach (var d in w.Days)
        {
            d.Seconds.Keys.Should().HaveCount(7);
            d.Seconds[EffectiveStatus.Production].Should().Be(d.Seconds.Values.Sum());
        }
        w.Seconds[EffectiveStatus.Production].Should().Be(w.Days.Sum(d => d.Seconds.Values.Sum()));
        w.Efficiency.Should().Be(100m);

        var s = report.ImmData.Single(i => i.ImmId == silent);
        s.Days.Should().OnlyContain(d => d.Seconds[EffectiveStatus.NoData] == d.Seconds.Values.Sum());
        s.Efficiency.Should().BeNull();
    }

    [Theory]
    [InlineData(null,      true,  false)]
    [InlineData("include", true,  true)]
    [InlineData("only",    false, true)]
    [InlineData("Only",    false, true)]   // регистр не важен
    public async Task Archive_Filter_Selects_Imms(string? archive, bool expectActive, bool expectArchived)
    {
        var active   = await SeedImmAsync(isActive: true,  workingAllAround: false);
        var archived = await SeedImmAsync(isActive: false, workingAllAround: false);
        var client = await ManagerClientAsync();

        var resp = await client.GetAsync(Url(Day, Day, new[] { active, archived }, archive));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var ids = (await resp.Content.ReadFromJsonAsync<EquipmentReportDto>())!.ImmData.Select(i => i.ImmId).ToList();

        ids.Contains(active).Should().Be(expectActive);
        ids.Contains(archived).Should().Be(expectArchived);
    }

    [Fact]
    public async Task Archived_Imm_Is_Marked_Inactive()
    {
        var archived = await SeedImmAsync(isActive: false, workingAllAround: false);
        var client = await ManagerClientAsync();

        var report = (await client.GetFromJsonAsync<EquipmentReportDto>(Url(Day, Day, new[] { archived }, "only")))!;

        report.ImmData.Should().ContainSingle().Which.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DateFrom_After_DateTo_Returns_400()
    {
        var client = await ManagerClientAsync();

        var resp = await client.GetAsync(Url(Day.AddDays(1), Day, Array.Empty<Guid>()));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
