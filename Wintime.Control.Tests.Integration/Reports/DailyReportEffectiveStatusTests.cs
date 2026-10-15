using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.DTOs.Report;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Reports;

/// <summary>
/// «Картина рабочего дня» показывает эффективное состояние ТПА — как карточка ТПА и
/// «Производительность оборудования», с теми же часами и эффективностью.
/// </summary>
[Collection("Integration")]
public class DailyReportEffectiveStatusTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public DailyReportEffectiveStatusTests(IntegrationTestFactory factory) => _factory = factory;

    private static readonly DateTime Date = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-12), DateTimeKind.Unspecified);

    [Fact]
    public async Task PastDay_HoursAndEfficiencyAreEffectiveStatus_AndMatchEquipmentReport()
    {
        Guid immId;
        DateTime d0; // начало суток завода (UTC)
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
            // Сутки отчёта — в поясе завода (зона смен), как в DailyReportTimelineTests.
            var tzId = db.Shifts.OrderBy(s => s.StartMinutes).Select(s => s.TimeZoneId).FirstOrDefault()
                       ?? "Europe/Moscow";
            var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
            d0 = TimeZoneInfo.ConvertTimeToUtc(Date, tz);
            DateTime H(double h) => d0.AddHours(h);

            var imm = new Core.Entities.Imm { Name = $"IMM-{Guid.NewGuid():N}", TemplateId = _factory.TestTemplateId, IsActive = true };
            var mold = new Mold { Name = "M-daily", FormId = $"FORM-{Guid.NewGuid():N}", Cavities = 1, IsActive = true };
            db.Imms.Add(imm);
            db.Molds.Add(mold);
            await db.SaveChangesAsync();

            // Наладка 7:30–8:00, выпуск 8:00–14:00.
            db.ShiftTasks.Add(new ShiftTask
            {
                ImmId = imm.Id, MoldId = mold.Id, PlanQuantity = 100, Status = TaskStatus.Completed,
                SetupStartedAt = H(7.5), StartedAt = H(8), CompletedAt = H(14)
            });
            // Сырой режим: manual 7:30–8 · auto 8–10 · offline 10–12 · idle 12–13 · auto 13–14 · idle 14–16 (без задания).
            db.ImmStatusHistory.AddRange(
                new ImmStatusHistory { ImmId = imm.Id, Status = ImmStatus.Manual,  ChangedAt = H(7.5), EndedAt = H(8) },
                new ImmStatusHistory { ImmId = imm.Id, Status = ImmStatus.Auto,    ChangedAt = H(8),   EndedAt = H(10) },
                new ImmStatusHistory { ImmId = imm.Id, Status = ImmStatus.Offline, ChangedAt = H(10),  EndedAt = H(12) },
                new ImmStatusHistory { ImmId = imm.Id, Status = ImmStatus.Idle,    ChangedAt = H(12),  EndedAt = H(13) },
                new ImmStatusHistory { ImmId = imm.Id, Status = ImmStatus.Auto,    ChangedAt = H(13),  EndedAt = H(14) },
                new ImmStatusHistory { ImmId = imm.Id, Status = ImmStatus.Idle,    ChangedAt = H(14),  EndedAt = H(16) });
            // Простой задания 12–13 (событие).
            db.Events.Add(new Event { ImmId = imm.Id, EventType = EventType.Downtime, StartTime = H(12), EndTime = H(13), IsAuto = true });
            await db.SaveChangesAsync();
            immId = imm.Id;
        }

        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);

        var daily = (await client.GetFromJsonAsync<DailyReportDto>(
            $"/api/reports/daily?date={Date:yyyy-MM-dd}&immId={immId}"))!;
        var item = daily.ImmData.Should().ContainSingle().Subject;

        // Работа 8–10 + 13–14 · Наладка 7:30–8 · Простой 12–13 · Нет связи 10–12 (ADR-0019) · Без задания 14–16.
        item.Seconds[EffectiveStatus.Production].Should().Be(3 * 3600);
        item.Seconds[EffectiveStatus.Setup].Should().Be(1800);
        item.Seconds[EffectiveStatus.Downtime].Should().Be(3600);
        item.Seconds[EffectiveStatus.Offline].Should().Be(2 * 3600);
        item.Seconds[EffectiveStatus.NoTask].Should().Be(2 * 3600);
        item.Seconds[EffectiveStatus.Unplanned].Should().Be(0);
        item.Seconds.Values.Sum().Should().Be(24 * 3600);
        // Работа / (всё − Нет связи − Нет данных) = 3 / (3 + 0.5 + 1 + 2) ч
        item.Efficiency.Should().Be(Math.Round(3m / 6.5m * 100, 2));

        var allowed = new[]
        {
            EffectiveStatus.Production, EffectiveStatus.Setup, EffectiveStatus.Downtime, EffectiveStatus.Unplanned,
            EffectiveStatus.NoTask, EffectiveStatus.Offline, EffectiveStatus.NoData
        };
        item.Timeline.Should().OnlyContain(t => allowed.Contains(t.Type));
        item.Timeline.Should().Contain(t => t.Type == EffectiveStatus.Offline && t.Start == d0.AddHours(10) && t.End == d0.AddHours(12));

        // Те же сутки в «Производительности оборудования» — те же часы и эффективность.
        var equipment = (await client.GetFromJsonAsync<EquipmentReportDto>(
            $"/api/reports/equipment?dateFrom={Date:yyyy-MM-dd}&dateTo={Date:yyyy-MM-dd}&immIds={immId}"))!;
        var eq = equipment.ImmData.Should().ContainSingle().Subject;
        item.Seconds.Should().BeEquivalentTo(eq.Seconds);
        item.Efficiency.Should().Be(eq.Efficiency);
    }

    [Fact]
    public async Task ExcelExport_HasEffectiveStatusHourColumns()
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);

        var resp = await client.PostAsJsonAsync("/api/reports/export/excel", new
        {
            reportType = "daily", dateFrom = Date, dateTo = Date
        });
        resp.EnsureSuccessStatusCode();

        using var wb = new ClosedXML.Excel.XLWorkbook(await resp.Content.ReadAsStreamAsync());
        var header = wb.Worksheets.First().RowsUsed().First(r => r.Cell(1).GetString() == "ТПА");
        header.Cells(1, 13).Select(c => c.GetString()).Should().Equal(
            "ТПА", "Пресс-форма", "План", "Факт", "Циклы",
            "Работа (ч)", "Наладка (ч)", "Простой (ч)", "Работа без задания (ч)", "Без задания (ч)", "Нет связи (ч)",
            "Эффективность %", "Сырьё (кг)");
    }
}
