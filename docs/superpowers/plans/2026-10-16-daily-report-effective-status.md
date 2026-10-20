# «Картина рабочего дня» на эффективном состоянии — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** «Картина рабочего дня» показывает эффективное состояние ТПА — те же состояния, цвета, подписи, часы и эффективность, что карточка ТПА и «Производительность оборудования».

**Architecture:** `ReportService.GetDailyReportAsync` перестаёт читать `ImmStatusHistory` напрямую: один вызов `IEffectiveStatusHistoryService.GatherAsync` на все ТПА, затем `EffectiveStatusTimeline.Build(..., gapAsNoData: true)` по окну диаграммы, часы — `DailyStatusBreakdown.Split` по окну периода, эффективность — `DailyStatusBreakdown.Efficiency`. Фронт берёт палитру из `REPORT_STATUS`.

**Tech Stack:** ASP.NET Core 9, EF Core 9/PostgreSQL, ClosedXML, xUnit + FluentAssertions (Integration, Testcontainers); Vue 3, Element Plus, ECharts, Vitest.

**Spec:** `docs/superpowers/specs/2026-10-16-daily-report-effective-status-design.md`

## Global Constraints

- Состояния и хронология — только через `IEffectiveStatusHistoryService.GatherAsync` + `EffectiveStatusTimeline.Build(..., gapAsNoData: true)`; сырые статусы в отчёте не читать.
- Окно диаграммы — `[period.TimelineStart, min(period.TimelineEnd, now))`; часы — `[period.PeriodStart, min(period.PeriodEnd, now))`.
- `TimelineItemDto.Type` — ключ эффективного состояния (`Production | Setup | Downtime | Unplanned | NoTask | Offline | NoData`).
- `DailyReportImmItemDto`: без `WorkTimeSeconds/SetupSeconds/DowntimeSeconds/OfflineSeconds`; `Seconds: Dictionary<string,int>` (7 ключей); `Efficiency: decimal?`.
- Эффективность — `DailyStatusBreakdown.Efficiency` (Работа / (всё − Нет связи − Нет данных)); `null` → «—».
- Часы за прошедшие сутки и эффективность совпадают с «Производительностью оборудования» за тот же день.
- Палитра на фронте — только `REPORT_STATUS` из `src/constants/effectiveStatus.js`; неизвестный тип — цвет `NoData`.
- Колонки часов (таблица и Excel): Работа · Наладка · Простой · Работа без задания · Без задания · Нет связи (ч) — `DailyStatusBreakdown.Keys` без `NoData`.
- Все `DateTime` в EF-запросах — `Kind=Utc`. Контракт меняется без совместимости.
- Версия перед PR: `0.14.1` (master `0.14.0`; это фикс — патч).
- Коммиты заканчивать строкой `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

---

### Task 1: Бэкенд — дневной отчёт на эффективном состоянии + Excel

**Files:**
- Modify: `Wintime.Control.Core/DTOs/Report/DailyReportImmItemDto.cs`
- Modify: `Wintime.Control.Core/DTOs/Report/TimelineItemDto.cs`
- Modify: `Wintime.Control.Infrastructure/Reports/ReportService.cs` (`GetDailyReportAsync` ~стр. 31–79; `GenerateDailyImmItemAsync` ~стр. 84–235; `MapStatusToType` ~стр. 273; Excel-ветка `DailyReportDto` ~стр. 683–730)
- Create: `Wintime.Control.Tests.Integration/Reports/DailyReportEffectiveStatusTests.cs`
- Modify: `Wintime.Control.Tests.Integration/Reports/DailyReportTimelineTests.cs` (стр. ~96)

**Interfaces:**
- Consumes: `IEffectiveStatusHistoryService.GatherAsync(ids, fromUtc, toUtc, effectiveTo, ct) → IReadOnlyDictionary<Guid, EffectiveStatusInputs>` (`Raw`, `Tasks`, `Downtimes`); `EffectiveStatusTimeline.Build(raw, tasks, downtimes, from, to, gapAsNoData)`; `DailyStatusBreakdown.Keys/Labels/Empty()/Split(segments, days)/Efficiency(dict)` (Infrastructure/Reports, internal); `FactoryDay(Date, StartUtc, EndUtc)` (internal record struct); `DailyReportPeriod(PeriodStart, PeriodEnd, TimelineStart, TimelineEnd)`.
- Produces: JSON `GET /api/reports/daily` → `immData[i]`: `{ …, seconds: { Production, Setup, Downtime, Unplanned, NoTask, Offline, NoData }, efficiency: number|null, timeline: [{ start, end, type: <EffectiveStatus key> }], … }` (без `workTimeSeconds/setupSeconds/downtimeSeconds/offlineSeconds`).

- [ ] **Step 1: Write the failing test**

Create `Wintime.Control.Tests.Integration/Reports/DailyReportEffectiveStatusTests.cs`:

```csharp
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
```

In `DailyReportTimelineTests.cs` replace the last assertion line (`item.WorkTimeSeconds.Should()...`) with:

```csharp
        item.Seconds[EffectiveStatus.Production].Should().BeLessThanOrEqualTo(maxExpectedWork);
```

(`using Wintime.Control.Core.Constants;` is already there.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~DailyReport"`
Expected: build FAIL — `'DailyReportImmItemDto' does not contain a definition for 'Seconds'`.

- [ ] **Step 3: DTOs**

`DailyReportImmItemDto.cs` — replace the four `…Seconds` properties and `Efficiency`:

```csharp
    /// <summary>Секунды по эффективным состояниям за период до «сейчас» (7 ключей, включая NoData) — как в «Производительности».</summary>
    public Dictionary<string, int> Seconds { get; set; } = new();
    /// <summary>Работа / (всё − Нет связи − Нет данных) × 100; null — известного времени нет.</summary>
    public decimal? Efficiency { get; set; }
```

`TimelineItemDto.cs` — doc of `Type`:

```csharp
    /// <summary>Ключ эффективного состояния: Production | Setup | Downtime | Unplanned | NoTask | Offline | NoData.</summary>
```

- [ ] **Step 4: Service**

In `GetDailyReportAsync`, after `var imms = await immQuery.ToListAsync(ct);` replace the loop with:

```csharp
        // Эффективное состояние — те же ряды, что у карточки ТПА и «Производительности» (ADR-0006/0019).
        // Открытые интервалы тянутся только до «сейчас», не до конца окна графика.
        var now = DateTime.UtcNow;
        var timelineEnd = period.TimelineEnd < now ? period.TimelineEnd : now;
        var inputs = await _effectiveStatusHistory.GatherAsync(
            imms.Select(i => i.Id).ToList(), period.TimelineStart, period.TimelineEnd, timelineEnd, ct);

        foreach (var imm in imms)
        {
            var item = await GenerateDailyImmItemAsync(imm, period, inputs[imm.Id], now, timelineEnd, ct);
            if (item != null)
                report.ImmData.Add(item);
        }
```

Change the signature of `GenerateDailyImmItemAsync` to:

```csharp
    private async Task<DailyReportImmItemDto?> GenerateDailyImmItemAsync(Imm imm, DailyReportPeriod period,
        EffectiveStatusInputs inputs, DateTime now, DateTime timelineEnd, CancellationToken ct)
```

Delete the `statusHistory` query (the `// История статусов ТПА за окно графика…` block), and replace the whole block from `// Расчёт времени по пяти состояниям и построение Timeline` through the end of the `foreach (var entry in statusHistory) { … }` loop with:

```csharp
        // Хронология эффективного состояния за окно графика; дыры в сырой истории — «Нет данных»,
        // как в «Производительности оборудования».
        var segments = EffectiveStatusTimeline.Build(
            inputs.Raw, inputs.Tasks, inputs.Downtimes, period.TimelineStart, timelineEnd, gapAsNoData: true);
        var timeline = segments
            .Select(s => new TimelineItemDto { Start = s.Start, End = s.End, Type = s.EffectiveStatus })
            .ToList();

        // Часы — по периоду (смена/сутки) до «сейчас»; та же раскладка, что в «Производительности».
        var aggregateEnd = periodEnd < now ? periodEnd : now;
        var seconds = aggregateEnd > periodStart
            ? DailyStatusBreakdown.Split(segments, new[] { new FactoryDay(periodStart, periodStart, aggregateEnd) })[0]
            : DailyStatusBreakdown.Empty();
```

Further down, the planned quantity uses `aggregateEnd` and `now` — keep them (they are now defined above; remove any duplicate `var now`/`var aggregateEnd`/`var timelineEnd` declarations left from the old block). Delete the old efficiency block (`// Эффективность: полезная работа / (работа + наладка + простои)` … `: 0;`). In the returned DTO replace `WorkTimeSeconds/SetupSeconds/DowntimeSeconds/OfflineSeconds/Efficiency = …` with:

```csharp
            Seconds = seconds,
            Efficiency = DailyStatusBreakdown.Efficiency(seconds),
```

Delete `MapStatusToType` (now unused).

- [ ] **Step 5: Excel**

In the `DailyReportDto` branch of the Excel export replace the headers array and the row loop with:

```csharp
            // Колонки часов — эффективные состояния без «Нет данных», как в «Производительности».
            var hourKeys = DailyStatusBreakdown.Keys.Where(k => k != EffectiveStatus.NoData).ToArray();
            var headers = new List<string> { "ТПА", "Пресс-форма", "План", "Факт", "Циклы" };
            headers.AddRange(hourKeys.Select(k => $"{DailyStatusBreakdown.Labels[k]} (ч)"));
            headers.AddRange(["Эффективность %", "Сырьё (кг)"]);
            for (int i = 0; i < headers.Count; i++)
            {
                worksheet.Cell(row, i + 1).Value = headers[i];
                worksheet.Cell(row, i + 1).Style.Font.Bold = true;
            }
            row++;

            var effCol = 6 + hourKeys.Length;
            foreach (var item in dailyReport.ImmData)
            {
                worksheet.Cell(row, 1).Value = item.ImmName;
                worksheet.Cell(row, 2).Value = item.MoldName;
                worksheet.Cell(row, 3).Value = item.PlanQuantity;
                worksheet.Cell(row, 4).Value = item.ActualQuantity;
                worksheet.Cell(row, 5).Value = item.CycleCount;
                for (var k = 0; k < hourKeys.Length; k++)
                    worksheet.Cell(row, 6 + k).Value = Math.Round(item.Seconds.GetValueOrDefault(hourKeys[k]) / 3600m, 2);
                if (item.Efficiency.HasValue)
                    worksheet.Cell(row, effCol).Value = Math.Round(item.Efficiency.Value, 2);
                else
                    worksheet.Cell(row, effCol).Value = "—";
                worksheet.Cell(row, effCol + 1).Value = Math.Round(item.RawMaterialKg, 2);
                row++;
            }
```

- [ ] **Step 6: Run to verify it passes**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~DailyReport|FullyQualifiedName~EquipmentReport"`
Expected: PASS (new 2 + existing daily/equipment tests).

If `PastDay_…` fails on exact hours: check the window is `[periodStart, aggregateEnd)` and `gapAsNoData: true`; do not change the expected numbers (they follow the matrix: InProgress+Offline without event → Offline, ADR-0019; Idle with downtime event → Downtime; Manual during Setup → Setup; Idle without task → NoTask).

- [ ] **Step 7: Full .NET suites**

Run: `dotnet test Wintime.Control.Tests.Unit` and `dotnet test Wintime.Control.Tests.Integration` → all PASS.

- [ ] **Step 8: Commit**

```bash
git add Wintime.Control.Core/DTOs/Report/DailyReportImmItemDto.cs Wintime.Control.Core/DTOs/Report/TimelineItemDto.cs Wintime.Control.Infrastructure/Reports/ReportService.cs Wintime.Control.Tests.Integration/Reports/DailyReportEffectiveStatusTests.cs Wintime.Control.Tests.Integration/Reports/DailyReportTimelineTests.cs
git commit -m "fix(reports): daily report on effective IMM status — same states, hours and efficiency as equipment report"
```

---

### Task 2: Фронт — палитра Gantt, колонки таблицы; руководство, CLAUDE.md, версия

**Files:**
- Modify: `Wintime-Control-Frontend/src/components/reports/GanttChart.vue` (`colorMap`/`typeLabels`/`legend` ~стр. 40–60; tooltip ~стр. 172; `fill` ~стр. 234)
- Create: `Wintime-Control-Frontend/src/components/reports/__tests__/GanttChart.spec.js`
- Modify: `Wintime-Control-Frontend/src/views/reports/DailyReportView.vue` (колонки ~стр. 78–99; `getEfficiencyType`)
- Create: `Wintime-Control-Frontend/src/views/reports/__tests__/DailyReportView.spec.js`
- Modify: `docs/user-guide/manager.md` (§8.1 «Картина рабочего дня», ~стр. 555–580)
- Modify: `CLAUDE.md` (раздел «Статусы ТПА»)
- Modify: `VERSION`

**Interfaces:**
- Consumes: JSON из Task 1 (`immData[i].seconds`, `efficiency: number|null`, `timeline[].type` — ключ состояния); `REPORT_STATUS`, `REPORT_STATUS_KEYS`, `EFFECTIVE_STATUS_KEYS` из `src/constants/effectiveStatus.js` (`REPORT_STATUS[k].label`, `.hex`; ключи: `Production, Setup, Downtime, Unplanned, NoTask, Offline, NoData`).

- [ ] **Step 1: Write the failing tests**

Create `Wintime-Control-Frontend/src/components/reports/__tests__/GanttChart.spec.js`:

```js
// @vitest-environment jsdom
import { describe, it, expect, vi } from 'vitest'
import { mount } from '@vue/test-utils'
import { REPORT_STATUS, REPORT_STATUS_KEYS } from '@/constants/effectiveStatus'

vi.mock('echarts', () => ({
  init: () => ({ setOption: vi.fn(), resize: vi.fn(), dispose: vi.fn() }),
  graphic: { clipRectByRect: vi.fn(() => ({})) }
}))

const { default: GanttChart } = await import('../GanttChart.vue')

describe('GanttChart — палитра эффективных состояний', () => {
  it('легенда — все состояния отчёта в цветах и подписях общей палитры', () => {
    const w = mount(GanttChart, { props: { data: [], shifts: [], date: '2026-10-01' } })

    const items = w.findAll('[data-test="legend-item"]')
    expect(items.map(i => i.text())).toEqual(REPORT_STATUS_KEYS.map(k => REPORT_STATUS[k].label))
    items.forEach((item, i) => {
      const swatch = item.find('span').element.style.background
      const expected = REPORT_STATUS[REPORT_STATUS_KEYS[i]].hex
      // jsdom нормализует цвет в rgb(...)
      const hex = expected.replace('#', '')
      const rgb = `rgb(${parseInt(hex.slice(0, 2), 16)}, ${parseInt(hex.slice(2, 4), 16)}, ${parseInt(hex.slice(4, 6), 16)})`
      expect(swatch).toBe(rgb)
    })
    expect(w.text()).not.toContain('Авария')
  })
})
```

Create `Wintime-Control-Frontend/src/views/reports/__tests__/DailyReportView.spec.js`:

```js
// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ElementPlus from 'element-plus'

vi.mock('vue-router', async (importOriginal) => ({
  ...(await importOriginal()),
  useRouter: () => ({ push: vi.fn() })
}))
vi.mock('@/api/imm', () => ({ immApi: { getList: vi.fn(() => Promise.resolve({ data: [] })) } }))
vi.mock('@/api/shifts', () => ({ shiftsApi: { getShifts: vi.fn(() => Promise.resolve({ data: [] })) } }))
vi.mock('@/api/reports', () => ({ reportsApi: { getDaily: vi.fn() } }))

const { reportsApi } = await import('@/api/reports')
const { default: DailyReportView } = await import('../DailyReportView.vue')

const H = 3600
const item = (o = {}) => ({
  immId: '1', immName: 'ТПА-4', moldName: 'ПФ', planQuantity: 10, actualQuantity: 8, cycleCount: 4,
  seconds: { Production: 3 * H, Setup: 0.5 * H, Downtime: H, Unplanned: 0, NoTask: 2 * H, Offline: 2 * H, NoData: 15.5 * H },
  efficiency: 46.15, rawMaterialKg: 1, downtimeDetails: [], timeline: [], ...o
})

async function mountWithReport(rows) {
  reportsApi.getDaily.mockResolvedValue({ data: { date: '2026-10-01', immData: rows } })
  const w = mount(DailyReportView, {
    global: { plugins: [ElementPlus], stubs: { GanttChart: true, 'el-date-picker': true } }
  })
  await flushPromises()
  await w.vm.loadReport()
  await flushPromises()
  return w
}

describe('DailyReportView — эффективные состояния', () => {
  beforeEach(() => { setActivePinia(createPinia()); vi.clearAllMocks() })

  it('таблица: шесть колонок часов эффективных состояний', async () => {
    const w = await mountWithReport([item()])

    const labels = w.findAllComponents({ name: 'ElTableColumn' }).map(c => c.props('label'))
    expect(labels).toEqual(expect.arrayContaining([
      'Работа (ч)', 'Наладка (ч)', 'Простой (ч)', 'Работа без задания (ч)', 'Без задания (ч)', 'Нет связи (ч)'
    ]))
    expect(w.text()).toContain('3.00')   // Работа
    expect(w.text()).toContain('2.00')   // Без задания / Нет связи
  })

  it('эффективность null → «—»', async () => {
    const w = await mountWithReport([item({ efficiency: null })])

    expect(w.find('[data-test="efficiency"]').text()).toBe('—')
  })
})
```

- [ ] **Step 2: Run to verify it fails**

Run (из `Wintime-Control-Frontend`): `npx vitest run src/components/reports/__tests__/GanttChart.spec.js src/views/reports/__tests__/DailyReportView.spec.js`
Expected: FAIL — no `[data-test="legend-item"]`; missing column labels; no `[data-test="efficiency"]`.

If mounting fails for reasons unrelated to this change (stores, router), fix only the test setup (mocks/stubs), not the components, and note it in the report.

- [ ] **Step 3: GanttChart**

Replace `colorMap`, `typeLabels` and `legend` with:

```js
import { REPORT_STATUS, REPORT_STATUS_KEYS } from '@/constants/effectiveStatus'

// Цвета и подписи — общая палитра эффективных состояний (как карточка ТПА и «Производительность»).
const statusMeta = (type) => REPORT_STATUS[type] ?? REPORT_STATUS.NoData

const legend = REPORT_STATUS_KEYS.map(type => ({ type, color: REPORT_STATUS[type].hex, label: REPORT_STATUS[type].label }))
```

In the template add `data-test="legend-item"` to the legend `div` (`v-for="item in legend"`). In the tooltip formatter replace `typeLabels[type] ?? type` with `statusMeta(type).label`; in `renderItem` replace `fill: colorMap[type] ?? colorMap.offline` with `fill: statusMeta(type).hex`. Remove any other remaining references to `colorMap`/`typeLabels`. The legend row uses `flex gap-5 justify-center` — add `flex-wrap` so 7 items fit on narrow screens.

- [ ] **Step 4: DailyReportView**

Replace the three columns «Работа (ч)», «Наладка (ч)», «Простой (ч)» with a loop over the hour keys and make efficiency null-safe:

```vue
        <el-table-column
          v-for="k in HOUR_KEYS"
          :key="k"
          :label="`${EFFECTIVE_STATUS[k].label} (ч)`"
          width="110"
          align="right"
        >
          <template #default="{ row }">
            {{ ((row.seconds?.[k] ?? 0) / 3600).toFixed(2) }}
          </template>
        </el-table-column>
        <el-table-column label="Эффективность" width="120">
          <template #default="{ row }">
            <el-tag v-if="row.efficiency != null" :type="getEfficiencyType(row.efficiency)" data-test="efficiency">
              {{ row.efficiency.toFixed(1) }}%
            </el-tag>
            <span v-else data-test="efficiency">—</span>
          </template>
        </el-table-column>
```

In `<script setup>`:

```js
import { EFFECTIVE_STATUS, EFFECTIVE_STATUS_KEYS } from '@/constants/effectiveStatus'

// Колонки часов — эффективные состояния (как в «Производительности»), «Нет данных» не показываем.
const HOUR_KEYS = EFFECTIVE_STATUS_KEYS
```

(`EFFECTIVE_STATUS_KEYS` = `Production, Setup, Downtime, Unplanned, NoTask, Offline` — без `NoData`.)

- [ ] **Step 5: Run to verify it passes**

Run: `npx vitest run src/components/reports/__tests__/GanttChart.spec.js src/views/reports/__tests__/DailyReportView.spec.js`
Expected: PASS, 3 tests.

- [ ] **Step 6: Docs and version**

`docs/user-guide/manager.md`, §8.1 «Картина рабочего дня» — the bullet list of table columns: replace `**Работа (ч)**, **Наладка (ч)**, **Простой (ч)**;` with `**Работа**, **Наладка**, **Простой**, **Работа без задания**, **Без задания**, **Нет связи** (ч) — те же состояния и цвета, что в карточке ТПА;`, and replace the «Эффективность» bullet with `**Эффективность** — доля работы во времени, когда ТПА был на связи (как в «Производительности оборудования»);`. If the section mentions «Авария» in the legend, remove it.

`CLAUDE.md`, section «Статусы ТПА», after the paragraph about the effective state add:

```markdown
Все отчёты по ТПА («Картина рабочего дня», «Производительность оборудования») и карточка ТПА показывают
**эффективное состояние** (одна палитра `effectiveStatus.js`); сырой режим — только `ImmStatusHistory`/телеметрия.
```

`VERSION`: `0.14.1` with a trailing newline.

- [ ] **Step 7: Full frontend suite and build**

Run (из `Wintime-Control-Frontend`): `npx vitest run` and `npm run build` → all PASS.

- [ ] **Step 8: Commit**

```bash
git add Wintime-Control-Frontend/src/components/reports/GanttChart.vue Wintime-Control-Frontend/src/components/reports/__tests__/GanttChart.spec.js Wintime-Control-Frontend/src/views/reports/DailyReportView.vue Wintime-Control-Frontend/src/views/reports/__tests__/DailyReportView.spec.js docs/user-guide/manager.md CLAUDE.md
git commit -m "fix(reports-ui): daily report Gantt and table use effective status palette and columns"
git add VERSION
git commit -m "build: bump version to 0.14.1"
```
