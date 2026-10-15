using System.Net;
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

[Collection("Integration")]
public class DailyReportTimelineTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public DailyReportTimelineTests(IntegrationTestFactory factory) => _factory = factory;

    /// <summary>
    /// Регрессия: незакрытый (текущий) статус в отчёте "Картина рабочего дня" не должен
    /// проецироваться в будущее до конца суток. Открытый статус тянется только до «сейчас».
    /// </summary>
    [Fact]
    public async Task Daily_Report_Clamps_Open_Status_To_Now_Not_EndOfDay()
    {
        var now = DateTime.UtcNow;

        Guid immId;
        DateTime today, dayStartUtc, statusChangedAt;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();

            // Отчёт строит сутки в поясе завода (зона смен, по умолчанию Europe/Moscow), а не в UTC:
            // «сегодня» и его начало считаем так же, иначе около полуночи завода статус попадает в другие сутки.
            var tzId = db.Shifts.OrderBy(s => s.StartMinutes).Select(s => s.TimeZoneId).FirstOrDefault()
                       ?? "Europe/Moscow";
            var tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
            today = TimeZoneInfo.ConvertTimeFromUtc(now, tz).Date; // отчёт за сегодня → periodEnd в будущем
            dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(today, DateTimeKind.Unspecified), tz);
            statusChangedAt = now.AddHours(-2) < dayStartUtc ? dayStartUtc : now.AddHours(-2);

            var imm = new Core.Entities.Imm
            {
                Name = $"IMM-{Guid.NewGuid():N}", TemplateId = _factory.TestTemplateId, IsActive = true
            };
            db.Imms.Add(imm);

            var mold = new Mold { Name = "M1", FormId = $"FORM-{Guid.NewGuid():N}", Cavities = 1, IsActive = true };
            db.Molds.Add(mold);
            await db.SaveChangesAsync();

            // Открытый (текущий) статус Auto — EndedAt == null
            db.ImmStatusHistory.Add(new ImmStatusHistory
            { ImmId = imm.Id, Status = ImmStatus.Auto, ChangedAt = statusChangedAt, EndedAt = null });

            db.ShiftTasks.Add(new ShiftTask
            {
                ImmId = imm.Id, MoldId = mold.Id, PlanQuantity = 100,
                Status = TaskStatus.InProgress, SetupStartedAt = statusChangedAt,
                StartedAt = statusChangedAt, CompletedAt = null
            });
            await db.SaveChangesAsync();
            immId = imm.Id;
        }

        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);

        var url = $"/api/reports/daily?date={today:yyyy-MM-dd}&immId={immId}";
        var resp = await client.GetAsync(url);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Средний цикл в отчёте не отдаётся: он смотрится по заданию (вкладка «Результаты выполнения»).
        (await resp.Content.ReadAsStringAsync()).Should().NotContainEquivalentOf("avgCycleTime");

        var report = await resp.Content.ReadFromJsonAsync<DailyReportDto>();
        report.Should().NotBeNull();
        var item = report!.ImmData.Should().ContainSingle().Subject;
        item.Timeline.Should().NotBeEmpty();

        // Ни один сегмент таймлайна не должен заканчиваться в будущем.
        var upperBound = DateTime.UtcNow.AddSeconds(5);
        item.Timeline.Should().OnlyContain(t => t.End <= upperBound);

        // И конкретно — открытый статус не растянут до конца суток завода (periodEnd = начало завтрашних суток).
        var endOfDayUtc = dayStartUtc.AddDays(1);
        item.Timeline.Max(t => t.End).Should().BeBefore(endOfDayUtc);

        // Отработанное время не должно быть раздуто проекцией в будущее.
        var maxExpectedWork = (int)(DateTime.UtcNow - statusChangedAt).TotalSeconds + 5;
        item.Seconds[EffectiveStatus.Production].Should().BeLessThanOrEqualTo(maxExpectedWork);
    }
}
