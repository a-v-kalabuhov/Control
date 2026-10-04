# Отчёт «Производительность оборудования» по каждому ТПА — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Заменить агрегированную диаграмму отчёта «Производительность оборудования» поднёвной диаграммой эффективного статуса (+ «Нет данных») для каждого ТПА, добавить фильтры ТПА и архивных ТПА.

**Architecture:** Бэкенд переиспользует `EffectiveStatusTimeline.Build` (флаг `gapAsNoData`) поверх входов, собранных пакетно новым `IEffectiveStatusHistoryService` (вынесен из `ImmController`); чистая функция `DailyStatusBreakdown.Split` режет таймлайн по суткам завода, дополняя непокрытое время `NoData`. Фронт рендерит список карточек `EquipmentImmRow` (ECharts, шкала 0–24) и итоговую таблицу; метрики парка — чистые функции в `utils/equipmentReport.js`.

**Tech Stack:** ASP.NET Core 9, EF Core 9 + Npgsql, xUnit + FluentAssertions, ClosedXML; Vue 3, Pinia, Element Plus, ECharts 6, Vitest + @vue/test-utils.

**Spec:** `docs/superpowers/specs/2026-10-03-equipment-report-per-imm-design.md`

## Global Constraints

- Ветка: `feature/equipment-report-per-imm` (уже создана, спека закоммичена). Push в `master` запрещён — только PR.
- 7 категорий, порядок везде одинаковый: `Production, Setup, Downtime, Unplanned, NoTask, Offline, NoData`.
- Подписи: Работа · Наладка · Простой · Работа без задания · Без задания · Нет связи · Нет данных.
- Эффективность ТПА = Работа / (длина периода − Нет связи − Нет данных) × 100; знаменатель 0 → `null` («—»).
- Эффективность парка = Σ Работа / Σ известного времени (взвешенная). Проблемные: < 70 % и < 50 %, `null` не учитывается.
- Все `DateTime` в EF-запросах — `Kind=Utc` (CLAUDE.md).
- `IsActive` — архивный флаг; архив: `Exclude` (по умолчанию) / `Include` / `Only`.
- `EFFECTIVE_STATUS` на фронте **не менять** (дашборд); «Нет данных» — отдельный `REPORT_STATUS`.
- Дашборд и телеметрический таймлайн поведения не меняют (`gapAsNoData` по умолчанию `false`).
- Коммиты заканчиваются строкой `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Команды

```powershell
dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~<Class>"
dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~<Class>"   # нужен Postgres тестов
cd Wintime-Control-Frontend; npx vitest run <path>
```

## Карта файлов

| Файл | Действие | Ответственность |
|---|---|---|
| `Wintime.Control.Core/Constants/EffectiveStatus.cs` | Modify | константа `NoData` |
| `Wintime.Control.Core/Policies/EffectiveStatusTimeline.cs` | Modify | флаг `gapAsNoData` |
| `Wintime.Control.Infrastructure/Reports/DailyStatusBreakdown.cs` | Create | ключи/подписи, нарезка по суткам, эффективность |
| `Wintime.Control.Core/Interfaces/IEffectiveStatusHistoryService.cs` | Create | контракт пакетного сбора входов + `EffectiveStatusInputs` |
| `Wintime.Control.Infrastructure/Services/EffectiveStatusHistoryService.cs` | Create | 3 запроса `ImmId IN (…)` |
| `Wintime.Control.API/Controllers/ImmController.cs` | Modify | использовать сервис, удалить приватный сбор |
| `Wintime.Control.API/Program.cs` | Modify | DI |
| `Wintime.Control.Core/Enums/ArchiveFilter.cs` | Create | enum фильтра архива |
| `Wintime.Control.Core/DTOs/Report/EquipmentReport*.cs` | Modify/Create/Delete | новый контракт |
| `Wintime.Control.Core/DTOs/Report/ExportReportRequestDto.cs` | Modify | `Archive` |
| `Wintime.Control.Infrastructure/Reports/IReportService.cs`, `ReportService.cs` | Modify | новый расчёт + Excel |
| `Wintime.Control.API/Controllers/ReportsController.cs` | Modify | `archive`, 400 |
| `Wintime-Control-Frontend/src/constants/effectiveStatus.js` | Modify | `NO_DATA_STATUS`, `REPORT_STATUS(_KEYS)` |
| `Wintime-Control-Frontend/src/api/reports.js` | Modify | `paramsSerializer` |
| `Wintime-Control-Frontend/src/utils/equipmentReport.js` | Create | метрики, форматирование, опция диаграммы |
| `Wintime-Control-Frontend/src/stores/reports.js` | Modify | геттеры через utils |
| `Wintime-Control-Frontend/src/components/reports/EffectiveStatusDayChart.vue` | Create | диаграмма одного ТПА |
| `Wintime-Control-Frontend/src/components/reports/EquipmentImmRow.vue` | Create | строка ТПА |
| `Wintime-Control-Frontend/src/views/reports/EquipmentReportView.vue` | Rewrite | фильтры, KPI, список, таблица |
| `Wintime-Control-Frontend/src/components/reports/BarChart.vue` | Delete | больше не используется |

---

### Task 1: `NoData` и флаг `gapAsNoData` в `EffectiveStatusTimeline`

**Files:**
- Modify: `Wintime.Control.Core/Constants/EffectiveStatus.cs` (после строки `Offline`)
- Modify: `Wintime.Control.Core/Policies/EffectiveStatusTimeline.cs:19-57`
- Test: `Wintime.Control.Tests.Unit/Policies/EffectiveStatusTimelineTests.cs`

**Interfaces:**
- Produces: `EffectiveStatus.NoData == "NoData"`; `EffectiveStatusTimeline.Build(IReadOnlyList<RawSegment> raw, IReadOnlyList<TaskInterval> tasks, IReadOnlyList<Interval> downtimes, DateTime from, DateTime to, bool gapAsNoData = false)`.

- [ ] **Step 1: Write the failing tests** — добавить в конец класса `EffectiveStatusTimelineTests`:

```csharp
    [Fact]
    public void Build_GapAsNoData_GapIsNoData_EvenWithOpenTask()
    {
        // Сырой: auto 0–20, дыра 20–40, auto 40–60. Задание InProgress всё время.
        var raw = new[]
        {
            new RawSegment(ImmStatus.Auto, M(0),  M(20)),
            new RawSegment(ImmStatus.Auto, M(40), M(60)),
        };
        var tasks = new[] { new TaskInterval(ActiveTaskStatus.InProgress, M(0), M(60)) };

        var result = EffectiveStatusTimeline.Build(
            raw, tasks, System.Array.Empty<Interval>(), M(0), M(60), gapAsNoData: true);

        result.Should().HaveCount(3);
        result[0].Should().BeEquivalentTo(new EffectiveSegment(EffectiveStatus.Production, M(0),  M(20)));
        result[1].Should().BeEquivalentTo(new EffectiveSegment(EffectiveStatus.NoData,     M(20), M(40)));
        result[2].Should().BeEquivalentTo(new EffectiveSegment(EffectiveStatus.Production, M(40), M(60)));
    }

    [Fact]
    public void Build_GapWithoutFlag_KeepsLegacyOfflineBehaviour()
    {
        // Без флага дыра в истории по-прежнему трактуется как Offline (дашборд не меняется).
        var result = EffectiveStatusTimeline.Build(
            System.Array.Empty<RawSegment>(), System.Array.Empty<TaskInterval>(),
            System.Array.Empty<Interval>(), M(0), M(60));

        result.Should().ContainSingle();
        result[0].EffectiveStatus.Should().Be(EffectiveStatus.Offline);
    }

    [Fact]
    public void Build_GapAsNoData_ExplicitOfflineStaysOffline()
    {
        var raw = new[] { new RawSegment(ImmStatus.Offline, M(0), M(60)) };

        var result = EffectiveStatusTimeline.Build(
            raw, System.Array.Empty<TaskInterval>(), System.Array.Empty<Interval>(), M(0), M(60), gapAsNoData: true);

        result.Should().ContainSingle();
        result[0].EffectiveStatus.Should().Be(EffectiveStatus.Offline);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~EffectiveStatusTimelineTests"`
Expected: FAIL — compile error `'EffectiveStatus' does not contain a definition for 'NoData'` / no parameter `gapAsNoData`.

- [ ] **Step 3: Implement**

`EffectiveStatus.cs` — после `Offline`:

```csharp
    public const string NoData     = "NoData";     // Нет данных — только отчёты: нет ни одной записи статуса
```

`EffectiveStatusTimeline.cs` — сигнатура и тело цикла:

```csharp
    /// <param name="gapAsNoData">
    /// true — под-интервал без единого сырого сегмента = <see cref="EffectiveStatus.NoData"/>
    /// (задание не учитывается: без телеметрии неизвестно, работал ли ТПА). false — как раньше,
    /// дыра трактуется как Offline (дашборд, телеметрия).
    /// </param>
    public static IReadOnlyList<EffectiveSegment> Build(
        IReadOnlyList<RawSegment> raw,
        IReadOnlyList<TaskInterval> tasks,
        IReadOnlyList<Interval> downtimes,
        DateTime from, DateTime to,
        bool gapAsNoData = false)
```

Заменить в цикле блок вычисления `eff` (строки `var rawMode = …` … `var eff = ImmEffectiveStatus.Resolve(...)`) на:

```csharp
            var rawSeg = raw.FirstOrDefault(s => s.Start <= mid && mid < s.End);

            string eff;
            if (rawSeg == null && gapAsNoData)
            {
                eff = EffectiveStatus.NoData;
            }
            else
            {
                var rawMode = rawSeg?.Status ?? ImmStatus.Offline;
                var task = tasks.FirstOrDefault(t => t.Start <= mid && mid < t.End)?.Status
                           ?? ActiveTaskStatus.None;
                var hasDowntime = downtimes.Any(d => d.Start <= mid && mid < d.End);

                eff = ImmEffectiveStatus.Resolve(rawMode, task, hasDowntime, thresholdPassed: false);
            }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~EffectiveStatus"`
Expected: PASS (все тесты `EffectiveStatusTimelineTests` и `ImmEffectiveStatusTests`).

- [ ] **Step 5: Commit**

```powershell
git add Wintime.Control.Core/Constants/EffectiveStatus.cs Wintime.Control.Core/Policies/EffectiveStatusTimeline.cs Wintime.Control.Tests.Unit/Policies/EffectiveStatusTimelineTests.cs
git commit -m "feat(effective-status): NoData status and gapAsNoData timeline option"
```

---

### Task 2: `DailyStatusBreakdown` — нарезка по суткам и эффективность

**Files:**
- Create: `Wintime.Control.Infrastructure/Reports/DailyStatusBreakdown.cs`
- Test: `Wintime.Control.Tests.Unit/Reports/DailyStatusBreakdownTests.cs`

**Interfaces:**
- Consumes: `EffectiveSegment` (Core.Policies), `EffectiveStatus.NoData` (Task 1), `FactoryDay(DateTime Date, DateTime StartUtc, DateTime EndUtc)` (internal, `Infrastructure/Reports/FactoryCalendar.cs`).
- Produces (internal, видно Tests.Unit через `InternalsVisibleTo`):
  - `DailyStatusBreakdown.Keys : string[]` — 7 ключей в порядке Global Constraints;
  - `DailyStatusBreakdown.Labels : IReadOnlyDictionary<string,string>`;
  - `DailyStatusBreakdown.Empty() : Dictionary<string,int>` — 7 ключей по 0;
  - `DailyStatusBreakdown.Split(IReadOnlyList<EffectiveSegment> segments, IReadOnlyList<FactoryDay> days) : List<Dictionary<string,int>>`;
  - `DailyStatusBreakdown.Efficiency(IReadOnlyDictionary<string,int> seconds) : decimal?`.

- [ ] **Step 1: Write the failing tests**

```csharp
using FluentAssertions;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Reports;
using Xunit;

namespace Wintime.Control.Tests.Unit.Reports;

public class DailyStatusBreakdownTests
{
    private static readonly DateTime D0 = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static DateTime H(double h) => D0.AddHours(h);
    private static FactoryDay Day(double startH, double endH) =>
        new(DateTime.SpecifyKind(H(startH).Date, DateTimeKind.Unspecified), H(startH), H(endH));

    [Fact]
    public void Split_SegmentAcrossMidnight_IsDividedBetweenDays()
    {
        var segments = new[] { new EffectiveSegment(EffectiveStatus.Production, H(20), H(28)) };
        var days = new[] { Day(0, 24), Day(24, 48) };

        var result = DailyStatusBreakdown.Split(segments, days);

        result.Should().HaveCount(2);
        result[0][EffectiveStatus.Production].Should().Be(4 * 3600);
        result[0][EffectiveStatus.NoData].Should().Be(20 * 3600);
        result[1][EffectiveStatus.Production].Should().Be(4 * 3600);
        result[1][EffectiveStatus.NoData].Should().Be(20 * 3600);
    }

    [Fact]
    public void Split_UncoveredTailOfDay_IsNoData()
    {
        // Таймлайн обрезан «сейчас» = 10:00 — остаток суток неизвестен.
        var segments = new[] { new EffectiveSegment(EffectiveStatus.Setup, H(0), H(10)) };

        var result = DailyStatusBreakdown.Split(segments, new[] { Day(0, 24) });

        result[0][EffectiveStatus.Setup].Should().Be(10 * 3600);
        result[0][EffectiveStatus.NoData].Should().Be(14 * 3600);
    }

    [Theory]
    [InlineData(23)]
    [InlineData(24)]
    [InlineData(25)]
    public void Split_SumOfDayEqualsDayLength(int dayHours)
    {
        var segments = new[]
        {
            new EffectiveSegment(EffectiveStatus.Offline,    H(0), H(5)),
            new EffectiveSegment(EffectiveStatus.Production, H(5), H(dayHours)),
        };

        var result = DailyStatusBreakdown.Split(segments, new[] { Day(0, dayHours) });

        result[0].Values.Sum().Should().Be(dayHours * 3600);
        result[0].Keys.Should().BeEquivalentTo(DailyStatusBreakdown.Keys);
    }

    [Fact]
    public void Split_NoSegments_WholeDayNoData()
    {
        var result = DailyStatusBreakdown.Split(System.Array.Empty<EffectiveSegment>(), new[] { Day(0, 24) });

        result[0][EffectiveStatus.NoData].Should().Be(86400);
    }

    [Fact]
    public void Efficiency_ProductionOverKnownTime()
    {
        var s = DailyStatusBreakdown.Empty();
        s[EffectiveStatus.Production] = 6 * 3600;
        s[EffectiveStatus.NoTask]     = 6 * 3600;  // снижает эффективность
        s[EffectiveStatus.Offline]    = 6 * 3600;  // не входит в знаменатель
        s[EffectiveStatus.NoData]     = 6 * 3600;  // не входит в знаменатель

        DailyStatusBreakdown.Efficiency(s).Should().Be(50m);
    }

    [Fact]
    public void Efficiency_NoKnownTime_IsNull()
    {
        var s = DailyStatusBreakdown.Empty();
        s[EffectiveStatus.Offline] = 3600;
        s[EffectiveStatus.NoData]  = 3600;

        DailyStatusBreakdown.Efficiency(s).Should().BeNull();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~DailyStatusBreakdownTests"`
Expected: FAIL — `The name 'DailyStatusBreakdown' does not exist`.

- [ ] **Step 3: Implement** `Wintime.Control.Infrastructure/Reports/DailyStatusBreakdown.cs`:

```csharp
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Policies;

namespace Wintime.Control.Infrastructure.Reports;

/// <summary>
/// Раскладка таймлайна эффективного состояния по локальным суткам завода (отчёт
/// «Производительность оборудования»). Непокрытое сегментами время суток — в т.ч. будущее
/// после «сейчас» — <see cref="EffectiveStatus.NoData"/>, поэтому сумма по суткам всегда
/// равна длине суток (24 ч; 23/25 ч в сутки перевода часов).
/// </summary>
internal static class DailyStatusBreakdown
{
    /// <summary>Категории столбца; порядок = порядок в стеке диаграммы и колонок Excel.</summary>
    internal static readonly string[] Keys =
    [
        EffectiveStatus.Production, EffectiveStatus.Setup, EffectiveStatus.Downtime,
        EffectiveStatus.Unplanned, EffectiveStatus.NoTask, EffectiveStatus.Offline, EffectiveStatus.NoData
    ];

    internal static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        [EffectiveStatus.Production] = "Работа",
        [EffectiveStatus.Setup]      = "Наладка",
        [EffectiveStatus.Downtime]   = "Простой",
        [EffectiveStatus.Unplanned]  = "Работа без задания",
        [EffectiveStatus.NoTask]     = "Без задания",
        [EffectiveStatus.Offline]    = "Нет связи",
        [EffectiveStatus.NoData]     = "Нет данных",
    };

    internal static Dictionary<string, int> Empty() => Keys.ToDictionary(k => k, _ => 0);

    internal static List<Dictionary<string, int>> Split(
        IReadOnlyList<EffectiveSegment> segments, IReadOnlyList<FactoryDay> days)
    {
        var result = new List<Dictionary<string, int>>(days.Count);
        foreach (var day in days)
        {
            var secs = Empty();
            var covered = 0;
            foreach (var s in segments)
            {
                var start = s.Start > day.StartUtc ? s.Start : day.StartUtc;
                var end   = s.End   < day.EndUtc   ? s.End   : day.EndUtc;
                if (end <= start) continue;

                var d = (int)(end - start).TotalSeconds;
                secs[s.EffectiveStatus] += d;
                covered += d;
            }

            // Остаток (не покрыт таймлайном + усечение долей секунды) — «Нет данных».
            secs[EffectiveStatus.NoData] += (int)(day.EndUtc - day.StartUtc).TotalSeconds - covered;
            result.Add(secs);
        }
        return result;
    }

    /// <summary>Работа / (всё время − Нет связи − Нет данных) × 100; нет известного времени → null.</summary>
    internal static decimal? Efficiency(IReadOnlyDictionary<string, int> seconds)
    {
        var known = seconds.Values.Sum() - seconds[EffectiveStatus.Offline] - seconds[EffectiveStatus.NoData];
        return known > 0
            ? Math.Round((decimal)seconds[EffectiveStatus.Production] / known * 100, 2)
            : null;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~DailyStatusBreakdownTests"`
Expected: PASS (8 тестов, Theory считается за 3).

- [ ] **Step 5: Commit**

```powershell
git add Wintime.Control.Infrastructure/Reports/DailyStatusBreakdown.cs Wintime.Control.Tests.Unit/Reports/DailyStatusBreakdownTests.cs
git commit -m "feat(reports): DailyStatusBreakdown splits effective timeline by factory days"
```

---

### Task 3: `IEffectiveStatusHistoryService` — пакетный сбор входов, вынос из `ImmController`

**Files:**
- Create: `Wintime.Control.Core/Interfaces/IEffectiveStatusHistoryService.cs`
- Create: `Wintime.Control.Infrastructure/Services/EffectiveStatusHistoryService.cs`
- Modify: `Wintime.Control.API/Controllers/ImmController.cs` (поля/конструктор строки 23-34; вызовы на строках 459 и 523; удалить метод 537-581)
- Modify: `Wintime.Control.API/Program.cs:172` (рядом с `IShiftService`)
- Test: `Wintime.Control.Tests.Integration/Imm/EffectiveStatusHistoryServiceTests.cs`

**Interfaces:**
- Consumes: `RawSegment`, `TaskInterval`, `Interval` (Core.Policies).
- Produces:
  - `public record EffectiveStatusInputs(IReadOnlyList<RawSegment> Raw, IReadOnlyList<TaskInterval> Tasks, IReadOnlyList<Interval> Downtimes);`
  - `Task<IReadOnlyDictionary<Guid, EffectiveStatusInputs>> IEffectiveStatusHistoryService.GatherAsync(IReadOnlyCollection<Guid> immIds, DateTime fromUtc, DateTime toUtc, DateTime effectiveTo, CancellationToken ct = default)` — ключ есть для **каждого** id из `immIds` (пустые ряды, если данных нет).

- [ ] **Step 1: Write the failing test** `Wintime.Control.Tests.Integration/Imm/EffectiveStatusHistoryServiceTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Imm;

[Collection("Integration")]
public class EffectiveStatusHistoryServiceTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public EffectiveStatusHistoryServiceTests(IntegrationTestFactory factory) => _factory = factory;

    [Fact]
    public async Task GatherAsync_ReturnsInputsPerImm_AndEmptyForImmWithoutData()
    {
        var from = new DateTime(2026, 6, 25, 8, 0, 0, DateTimeKind.Utc);
        var to   = from.AddHours(1);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();

        var withData = new Core.Entities.Imm { Name = $"IMM-{Guid.NewGuid():N}", TemplateId = _factory.TestTemplateId, IsActive = true };
        var empty    = new Core.Entities.Imm { Name = $"IMM-{Guid.NewGuid():N}", TemplateId = _factory.TestTemplateId, IsActive = true };
        var mold = new Mold { Name = "M1", FormId = $"FORM-{Guid.NewGuid():N}", Cavities = 1, IsActive = true };
        db.Imms.AddRange(withData, empty);
        db.Molds.Add(mold);
        await db.SaveChangesAsync();

        db.ImmStatusHistory.Add(new ImmStatusHistory
            { ImmId = withData.Id, Status = ImmStatus.Auto, ChangedAt = from, EndedAt = null });
        db.ShiftTasks.Add(new ShiftTask
        {
            ImmId = withData.Id, MoldId = mold.Id, PlanQuantity = 100,
            Status = TaskStatus.InProgress, SetupStartedAt = from, StartedAt = from.AddMinutes(10)
        });
        db.Events.Add(new Event
            { ImmId = withData.Id, EventType = EventType.Downtime, StartTime = from.AddMinutes(40), EndTime = null });
        await db.SaveChangesAsync();

        var service = scope.ServiceProvider.GetRequiredService<IEffectiveStatusHistoryService>();
        var result = await service.GatherAsync(new[] { withData.Id, empty.Id }, from, to, effectiveTo: to);

        result.Should().ContainKeys(withData.Id, empty.Id);

        var a = result[withData.Id];
        a.Raw.Should().ContainSingle().Which.End.Should().Be(to);          // открытый статус обрезан effectiveTo
        a.Tasks.Should().HaveCount(2);                                      // наладка + работа
        a.Downtimes.Should().ContainSingle().Which.End.Should().Be(to);

        var b = result[empty.Id];
        b.Raw.Should().BeEmpty();
        b.Tasks.Should().BeEmpty();
        b.Downtimes.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~EffectiveStatusHistoryServiceTests"`
Expected: FAIL — compile error `IEffectiveStatusHistoryService could not be found`.

- [ ] **Step 3: Implement**

`Wintime.Control.Core/Interfaces/IEffectiveStatusHistoryService.cs`:

```csharp
using Wintime.Control.Core.Policies;

namespace Wintime.Control.Core.Interfaces;

/// <summary>Входные ряды <see cref="EffectiveStatusTimeline.Build"/> для одного ТПА.</summary>
public record EffectiveStatusInputs(
    IReadOnlyList<RawSegment> Raw,
    IReadOnlyList<TaskInterval> Tasks,
    IReadOnlyList<Interval> Downtimes);

/// <summary>
/// Сбор историзированных рядов (сырой статус, интервалы заданий, простои) для реконструкции
/// эффективного состояния. Используется дашбордом ТПА и отчётами.
/// </summary>
public interface IEffectiveStatusHistoryService
{
    /// <summary>
    /// Входы за окно [<paramref name="fromUtc"/>, <paramref name="toUtc"/>) для нескольких ТПА тремя
    /// запросами. Открытые интервалы обрезаются по <paramref name="effectiveTo"/> (обычно min(to, now)).
    /// В результате есть ключ для каждого id из <paramref name="immIds"/>.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, EffectiveStatusInputs>> GatherAsync(
        IReadOnlyCollection<Guid> immIds, DateTime fromUtc, DateTime toUtc, DateTime effectiveTo,
        CancellationToken ct = default);
}
```

`Wintime.Control.Infrastructure/Services/EffectiveStatusHistoryService.cs` (логика перенесена из `ImmController.GatherEffectiveStatusInputsAsync` без изменений, только `ImmId IN`):

```csharp
using Microsoft.EntityFrameworkCore;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;

namespace Wintime.Control.Infrastructure.Services;

public class EffectiveStatusHistoryService : IEffectiveStatusHistoryService
{
    private readonly ControlDbContext _context;

    public EffectiveStatusHistoryService(ControlDbContext context) => _context = context;

    public async Task<IReadOnlyDictionary<Guid, EffectiveStatusInputs>> GatherAsync(
        IReadOnlyCollection<Guid> immIds, DateTime fromUtc, DateTime toUtc, DateTime effectiveTo,
        CancellationToken ct = default)
    {
        DateTime ClampEnd(DateTime? end) => (end ?? effectiveTo) > effectiveTo ? effectiveTo : (end ?? effectiveTo);

        var ids = immIds.Distinct().ToList();

        var rawRows = await _context.ImmStatusHistory
            .Where(h => ids.Contains(h.ImmId) && h.ChangedAt < toUtc && (h.EndedAt == null || h.EndedAt > fromUtc))
            .OrderBy(h => h.ChangedAt)
            .Select(h => new { h.ImmId, h.Status, h.ChangedAt, h.EndedAt })
            .ToListAsync(ct);

        var taskRows = await _context.ShiftTasks
            .Where(t => ids.Contains(t.ImmId) && t.SetupStartedAt != null && t.SetupStartedAt < toUtc)
            .Select(t => new { t.ImmId, t.SetupStartedAt, t.StartedAt, t.CompletedAt, t.ClosedAt })
            .ToListAsync(ct);

        var downtimeRows = await _context.Events
            .Where(e => ids.Contains(e.ImmId) && e.EventType == EventType.Downtime
                        && e.StartTime < toUtc && (e.EndTime == null || e.EndTime > fromUtc))
            .Select(e => new { e.ImmId, e.StartTime, e.EndTime })
            .ToListAsync(ct);

        var result = new Dictionary<Guid, EffectiveStatusInputs>(ids.Count);
        foreach (var id in ids)
        {
            var raw = rawRows
                .Where(r => r.ImmId == id)
                .Select(r => new RawSegment(r.Status, r.ChangedAt, ClampEnd(r.EndedAt)))
                .ToList();

            var tasks = new List<TaskInterval>();
            foreach (var t in taskRows.Where(t => t.ImmId == id))
            {
                var setupStart = t.SetupStartedAt!.Value;
                var setupEnd   = t.StartedAt ?? t.CompletedAt ?? t.ClosedAt ?? toUtc;
                tasks.Add(new TaskInterval(ActiveTaskStatus.Setup, setupStart, ClampEnd(setupEnd)));
                if (t.StartedAt != null)
                {
                    var workEnd = t.CompletedAt ?? t.ClosedAt ?? toUtc;
                    tasks.Add(new TaskInterval(ActiveTaskStatus.InProgress, t.StartedAt.Value, ClampEnd(workEnd)));
                }
            }

            var downtimes = downtimeRows
                .Where(d => d.ImmId == id)
                .Select(d => new Interval(d.StartTime, ClampEnd(d.EndTime)))
                .ToList();

            result[id] = new EffectiveStatusInputs(raw, tasks, downtimes);
        }

        return result;
    }
}
```

`Program.cs` — после `builder.Services.AddScoped<IShiftService, ShiftService>();`:

```csharp
builder.Services.AddScoped<IEffectiveStatusHistoryService, EffectiveStatusHistoryService>();
```

`ImmController.cs`:
1. Поле `private readonly IEffectiveStatusHistoryService _effectiveStatusHistory;`, параметр конструктора `IEffectiveStatusHistoryService effectiveStatusHistory` (добавить последним перед `ILogger<ImmController> logger`), присвоение в теле.
2. В `GetTelemetryDashboard` (строки 459-460) заменить на:

```csharp
        var inputs = (await _effectiveStatusHistory.GatherAsync(new[] { id }, fromUtc, toUtc, effectiveTo))[id];
        var statusSegments = EffectiveStatusTimeline.Build(inputs.Raw, inputs.Tasks, inputs.Downtimes, fromUtc, effectiveTo)
```

3. В `GetImmEffectiveStatusHistory` (строки 523-525) заменить на:

```csharp
        var inputs = (await _effectiveStatusHistory.GatherAsync(new[] { id }, fromUtc, toUtc, effectiveTo))[id];

        var segments = EffectiveStatusTimeline.Build(inputs.Raw, inputs.Tasks, inputs.Downtimes, fromUtc, effectiveTo);
```

4. Удалить приватный метод `GatherEffectiveStatusInputsAsync` целиком.

- [ ] **Step 4: Run tests to verify they pass (новый + регрессия контроллера)**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~EffectiveStatusHistory"`
Expected: PASS — `EffectiveStatusHistoryServiceTests` (1) и существующие `EffectiveStatusHistoryTests` (2).

Run: `dotnet build Wintime.Control.API`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```powershell
git add Wintime.Control.Core/Interfaces/IEffectiveStatusHistoryService.cs Wintime.Control.Infrastructure/Services/EffectiveStatusHistoryService.cs Wintime.Control.API/Controllers/ImmController.cs Wintime.Control.API/Program.cs Wintime.Control.Tests.Integration/Imm/EffectiveStatusHistoryServiceTests.cs
git commit -m "refactor(effective-status): extract batch input gathering into service"
```

---

### Task 4: Новый расчёт отчёта, контракт API, фильтр архива

**Files:**
- Create: `Wintime.Control.Core/Enums/ArchiveFilter.cs`
- Create: `Wintime.Control.Core/DTOs/Report/EquipmentReportDayDto.cs`
- Modify: `Wintime.Control.Core/DTOs/Report/EquipmentReportDto.cs`
- Modify: `Wintime.Control.Core/DTOs/Report/EquipmentReportImmItemDto.cs`
- Delete: `Wintime.Control.Core/DTOs/Report/EquipmentReportDailyItemDto.cs`
- Modify: `Wintime.Control.Core/DTOs/Report/ExportReportRequestDto.cs`
- Modify: `Wintime.Control.Infrastructure/Reports/IReportService.cs:24`
- Modify: `Wintime.Control.Infrastructure/Reports/ReportService.cs` (конструктор строки 14-21; метод 236-364)
- Modify: `Wintime.Control.API/Controllers/ReportsController.cs:48-64, 99, 127`
- Test: `Wintime.Control.Tests.Integration/Reports/EquipmentReportTests.cs`

**Interfaces:**
- Consumes: `IEffectiveStatusHistoryService.GatherAsync` (Task 3), `EffectiveStatusTimeline.Build(..., gapAsNoData)` (Task 1), `DailyStatusBreakdown.Split/Empty/Efficiency` (Task 2), `FactoryCalendar.Days`.
- Produces:
  - `enum ArchiveFilter { Exclude, Include, Only }`;
  - `IReportService.GetEquipmentReportAsync(DateTime dateFrom, DateTime dateTo, List<Guid>? immIds = null, ArchiveFilter archive = ArchiveFilter.Exclude, CancellationToken ct = default)` — `ArgumentException` при `dateFrom.Date > dateTo.Date`;
  - JSON: `{ dateFrom, dateTo, immData: [{ immId, immName, isActive, seconds: {Production…NoData}, totalCycles, avgCycleSeconds, efficiency|null, days: [{ date, seconds }] }] }`;
  - `GET /api/reports/equipment?dateFrom&dateTo&immIds=…&archive=exclude|include|only`; `ExportReportRequestDto.Archive`.

- [ ] **Step 1: Write the failing tests** `Wintime.Control.Tests.Integration/Reports/EquipmentReportTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~EquipmentReportTests"`
Expected: FAIL — compile errors (`Days`, `Seconds`, `IsActive`, `Efficiency` не существуют в DTO).

- [ ] **Step 3: Implement**

`Wintime.Control.Core/Enums/ArchiveFilter.cs`:

```csharp
namespace Wintime.Control.Core.Enums;

/// <summary>Фильтр архивных (IsActive = false) сущностей в отчётах.</summary>
public enum ArchiveFilter
{
    Exclude,  // только действующие (по умолчанию)
    Include,  // действующие и архивные
    Only      // только архивные
}
```

`EquipmentReportDto.cs`:

```csharp
namespace Wintime.Control.Core.DTOs.Report;

public class EquipmentReportDto
{
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public List<EquipmentReportImmItemDto> ImmData { get; set; } = new();
}
```

`EquipmentReportImmItemDto.cs`:

```csharp
namespace Wintime.Control.Core.DTOs.Report;

public class EquipmentReportImmItemDto
{
    public Guid ImmId { get; set; }
    public string? ImmName { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Секунды по эффективным статусам за период (7 ключей, включая NoData).</summary>
    public Dictionary<string, int> Seconds { get; set; } = new();

    public int TotalCycles { get; set; }
    public decimal AvgCycleSeconds { get; set; }

    /// <summary>Работа / известное время × 100; null — известного времени нет.</summary>
    public decimal? Efficiency { get; set; }

    public List<EquipmentReportDayDto> Days { get; set; } = new();
}
```

`EquipmentReportDayDto.cs`:

```csharp
namespace Wintime.Control.Core.DTOs.Report;

public class EquipmentReportDayDto
{
    /// <summary>Дата локальных суток завода (Kind=Utc, время 00:00).</summary>
    public DateTime Date { get; set; }

    /// <summary>Секунды по эффективным статусам за сутки (7 ключей, сумма = длина суток).</summary>
    public Dictionary<string, int> Seconds { get; set; } = new();
}
```

Удалить `EquipmentReportDailyItemDto.cs`: `git rm Wintime.Control.Core/DTOs/Report/EquipmentReportDailyItemDto.cs`.

`ExportReportRequestDto.cs` — добавить `using Wintime.Control.Core.Enums;` и поле:

```csharp
    public ArchiveFilter Archive { get; set; } = ArchiveFilter.Exclude; // для Equipment
```

`IReportService.cs:24` — заменить сигнатуру (добавить `using Wintime.Control.Core.Enums;`):

```csharp
    Task<EquipmentReportDto> GetEquipmentReportAsync(DateTime dateFrom, DateTime dateTo, List<Guid>? immIds = null,
        ArchiveFilter archive = ArchiveFilter.Exclude, CancellationToken ct = default);
```

`ReportService.cs` — добавить `using Wintime.Control.Core.Interfaces;` и `using Wintime.Control.Core.Policies;`; конструктор:

```csharp
    private readonly ControlDbContext _context;
    private readonly IEffectiveStatusHistoryService _effectiveStatusHistory;
    private readonly ILogger<ReportService> _logger;

    public ReportService(ControlDbContext context, IEffectiveStatusHistoryService effectiveStatusHistory,
        ILogger<ReportService> logger)
    {
        _context = context;
        _effectiveStatusHistory = effectiveStatusHistory;
        _logger = logger;
    }
```

Заменить метод `GetEquipmentReportAsync` целиком (строки 236-364, вместе с XML-комментарием):

```csharp
    /// <summary>
    /// Отчёт "Производительность оборудования": по каждому ТПА — секунды эффективного состояния
    /// (+ «Нет данных») по локальным суткам завода.
    /// </summary>
    public async Task<EquipmentReportDto> GetEquipmentReportAsync(DateTime dateFrom, DateTime dateTo,
        List<Guid>? immIds = null, ArchiveFilter archive = ArchiveFilter.Exclude, CancellationToken ct = default)
    {
        if (dateFrom.Date > dateTo.Date)
            throw new ArgumentException("Дата начала периода позже даты окончания");

        _logger.LogInformation("Generating equipment report from {From} to {To}, archive: {Archive}", dateFrom, dateTo, archive);

        // Сутки — локальные сутки завода (зона смен), не UTC
        var days = FactoryCalendar.Days(dateFrom, dateTo, await GetFactoryTimeZoneIdAsync(ct));
        var periodStart = days[0].StartUtc;
        var periodEnd   = days[^1].EndUtc;

        // Таймлайн строится только до «сейчас»; остаток суток Split заполнит «Нет данных».
        var nowUtc = DateTime.UtcNow;
        var windowEnd = periodEnd < nowUtc ? periodEnd : nowUtc;

        var immQuery = archive switch
        {
            ArchiveFilter.Include => _context.Imms.AsQueryable(),
            ArchiveFilter.Only    => _context.Imms.Where(i => !i.IsActive),
            _                     => _context.Imms.Where(i => i.IsActive)
        };
        if (immIds is { Count: > 0 })
            immQuery = immQuery.Where(i => immIds.Contains(i.Id));

        var imms = await immQuery.OrderBy(i => i.Name).ToListAsync(ct);
        var ids = imms.Select(i => i.Id).ToList();

        var inputs = await _effectiveStatusHistory.GatherAsync(ids, periodStart, periodEnd, windowEnd, ct);

        // Только закрытые успешные циклы: открытые исключены из длительностей до закрытия.
        var cycleDurations = (await _context.ImmCycles
                .Where(c => ids.Contains(c.ImmId) && c.IsSuccessful && c.EndTime != null
                    && c.StartTime >= periodStart && c.StartTime < periodEnd)
                .Select(c => new { c.ImmId, c.DurationSeconds })
                .ToListAsync(ct))
            .ToLookup(c => c.ImmId, c => c.DurationSeconds);

        var report = new EquipmentReportDto
        {
            DateFrom = DateTime.SpecifyKind(dateFrom.Date, DateTimeKind.Utc),
            DateTo   = DateTime.SpecifyKind(dateTo.Date, DateTimeKind.Utc),
        };

        foreach (var imm in imms)
        {
            var inp = inputs[imm.Id];
            var segments = EffectiveStatusTimeline.Build(
                inp.Raw, inp.Tasks, inp.Downtimes, periodStart, windowEnd, gapAsNoData: true);
            var perDay = DailyStatusBreakdown.Split(segments, days);

            var total = DailyStatusBreakdown.Empty();
            foreach (var day in perDay)
                foreach (var (key, secs) in day)
                    total[key] += secs;

            var durations = cycleDurations[imm.Id].ToList();

            report.ImmData.Add(new EquipmentReportImmItemDto
            {
                ImmId = imm.Id,
                ImmName = imm.Name,
                IsActive = imm.IsActive,
                Seconds = total,
                TotalCycles = durations.Count,
                AvgCycleSeconds = durations.Count > 0 ? (decimal)durations.Average() : 0m,
                Efficiency = DailyStatusBreakdown.Efficiency(total),
                Days = days.Select((day, i) => new EquipmentReportDayDto
                {
                    Date = DateTime.SpecifyKind(day.Date, DateTimeKind.Utc),
                    Seconds = perDay[i]
                }).ToList()
            });
        }

        return report;
    }
```

Временная заглушка Excel, чтобы проект собрался (ветку `EquipmentReportDto` в `ExportToExcelAsync` полностью переписывает Task 5): в строках 767-801 старые поля больше не существуют — замени тело ветки `else if (data is EquipmentReportDto equipmentReport)` на вызов `row = WriteEquipmentSheet(worksheet, row, equipmentReport);` и добавь в класс метод:

```csharp
    private static int WriteEquipmentSheet(IXLWorksheet worksheet, int row, EquipmentReportDto report)
    {
        worksheet.Cell(row, 1).Value = "Период:";
        worksheet.Cell(row, 1).Style.Font.Bold = true;
        worksheet.Cell(row, 2).Value = $"{report.DateFrom:dd.MM.yyyy} – {report.DateTo:dd.MM.yyyy}";
        return row + 1;
    }
```

`ReportsController.cs` — добавить `using Wintime.Control.Core.Enums;`; эндпоинт:

```csharp
    [HttpGet("equipment")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.Manager}")]
    public async Task<ActionResult<EquipmentReportDto>> GetEquipmentReport(
        [FromQuery] DateTime dateFrom,
        [FromQuery] DateTime dateTo,
        [FromQuery] List<Guid>? immIds = null,
        [FromQuery] ArchiveFilter archive = ArchiveFilter.Exclude)
    {
        try
        {
            var report = await _reportService.GetEquipmentReportAsync(dateFrom, dateTo, immIds, archive);
            return Ok(report);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "Некорректные параметры отчёта", message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Ошибка генерации отчёта", message = ex.Message });
        }
    }
```

Строки 99 и 127 (`"equipment" => …`):

```csharp
                "equipment" => await _reportService.GetEquipmentReportAsync(request.DateFrom, request.DateTo, request.ImmIds, request.Archive),
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~EquipmentReportTests"`
Expected: PASS (1 + 4 + 1 + 1 = 7 тестов).

Run: `dotnet test Wintime.Control.Tests.Unit`
Expected: PASS (ничего не сломано).

- [ ] **Step 5: Commit**

```powershell
git add -A Wintime.Control.Core Wintime.Control.Infrastructure/Reports Wintime.Control.API/Controllers/ReportsController.cs Wintime.Control.Tests.Integration/Reports/EquipmentReportTests.cs
git commit -m "feat(reports): equipment report per-IMM daily effective status with archive filter"
```

---

### Task 5: Excel-экспорт итоговой таблицы

**Files:**
- Modify: `Wintime.Control.Infrastructure/Reports/ReportService.cs` (метод `WriteEquipmentSheet` из Task 4)
- Test: `Wintime.Control.Tests.Integration/Reports/EquipmentReportTests.cs` (добавить тест)

**Interfaces:**
- Consumes: `DailyStatusBreakdown.Keys/Labels/Empty/Efficiency` (Task 2), `EquipmentReportDto` (Task 4).
- Produces: лист Excel: строки «Период», «Сформирован», пустая, заголовок `ТПА | <7 подписей> (ч) | Циклы | Ср. цикл (с) | Эффективность %`, строки ТПА, строка «Итого:».

- [ ] **Step 1: Write the failing test** — добавить в `EquipmentReportTests` (+ `using ClosedXML.Excel;`):

```csharp
    [Fact]
    public async Task Excel_Export_Has_Seven_Status_Columns_And_Total_Row()
    {
        var working  = await SeedImmAsync(isActive: true,  workingAllAround: true);
        var archived = await SeedImmAsync(isActive: false, workingAllAround: false);
        var client = await ManagerClientAsync();

        var resp = await client.PostAsJsonAsync("/api/reports/export/excel", new
        {
            reportType = "equipment",
            dateFrom = Day, dateTo = Day,
            immIds = new[] { working, archived },
            archive = "include"
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var wb = new XLWorkbook(await resp.Content.ReadAsStreamAsync());
        var ws = wb.Worksheets.First();
        var header = ws.RowsUsed().First(r => r.Cell(1).GetString() == "ТПА");

        header.Cells(2, 8).Select(c => c.GetString()).Should().Equal(
            "Работа (ч)", "Наладка (ч)", "Простой (ч)", "Работа без задания (ч)",
            "Без задания (ч)", "Нет связи (ч)", "Нет данных (ч)");
        header.Cell(9).GetString().Should().Be("Циклы");
        header.Cell(10).GetString().Should().Be("Ср. цикл (с)");
        header.Cell(11).GetString().Should().Be("Эффективность %");

        var names = ws.RowsUsed().Select(r => r.Cell(1).GetString()).ToList();
        names.Should().Contain(n => n.EndsWith("(архив)"));
        var total = ws.RowsUsed().Single(r => r.Cell(1).GetString() == "Итого:");
        total.Cell(11).GetValue<double>().Should().Be(100);   // Σ Работа / Σ известного = только working
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~Excel_Export_Has_Seven"`
Expected: FAIL — `Sequence contains no matching element` (нет строки заголовка «ТПА»).

- [ ] **Step 3: Implement** — заменить `WriteEquipmentSheet`:

```csharp
    private static int WriteEquipmentSheet(IXLWorksheet worksheet, int row, EquipmentReportDto report)
    {
        worksheet.Cell(row, 1).Value = "Период:";
        worksheet.Cell(row, 1).Style.Font.Bold = true;
        worksheet.Cell(row, 2).Value = $"{report.DateFrom:dd.MM.yyyy} – {report.DateTo:dd.MM.yyyy}";
        row++;

        worksheet.Cell(row, 1).Value = "Сформирован:";
        worksheet.Cell(row, 1).Style.Font.Bold = true;
        worksheet.Cell(row, 2).Value = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
        row += 2;

        var keys = DailyStatusBreakdown.Keys;
        var headers = new List<string> { "ТПА" };
        headers.AddRange(keys.Select(k => $"{DailyStatusBreakdown.Labels[k]} (ч)"));
        headers.AddRange(["Циклы", "Ср. цикл (с)", "Эффективность %"]);
        for (var i = 0; i < headers.Count; i++)
        {
            worksheet.Cell(row, i + 1).Value = headers[i];
            worksheet.Cell(row, i + 1).Style.Font.Bold = true;
        }
        row++;

        var cyclesCol = keys.Length + 2;
        foreach (var item in report.ImmData)
        {
            worksheet.Cell(row, 1).Value = item.IsActive ? item.ImmName : $"{item.ImmName} (архив)";
            for (var k = 0; k < keys.Length; k++)
                worksheet.Cell(row, k + 2).Value = Math.Round(item.Seconds.GetValueOrDefault(keys[k]) / 3600m, 2);
            worksheet.Cell(row, cyclesCol).Value = item.TotalCycles;
            worksheet.Cell(row, cyclesCol + 1).Value = Math.Round(item.AvgCycleSeconds, 1);
            if (item.Efficiency.HasValue)
                worksheet.Cell(row, cyclesCol + 2).Value = Math.Round(item.Efficiency.Value, 2);
            else
                worksheet.Cell(row, cyclesCol + 2).Value = "—";
            row++;
        }

        if (report.ImmData.Count > 0)
        {
            var totals = DailyStatusBreakdown.Empty();
            foreach (var item in report.ImmData)
                foreach (var k in keys)
                    totals[k] += item.Seconds.GetValueOrDefault(k);

            worksheet.Cell(row, 1).Value = "Итого:";
            for (var k = 0; k < keys.Length; k++)
                worksheet.Cell(row, k + 2).Value = Math.Round(totals[keys[k]] / 3600m, 2);
            worksheet.Cell(row, cyclesCol).Value = report.ImmData.Sum(i => i.TotalCycles);
            worksheet.Cell(row, cyclesCol + 1).Value = "—";
            var fleet = DailyStatusBreakdown.Efficiency(totals);   // взвешенная эффективность парка
            if (fleet.HasValue)
                worksheet.Cell(row, cyclesCol + 2).Value = Math.Round(fleet.Value, 2);
            else
                worksheet.Cell(row, cyclesCol + 2).Value = "—";
            for (var c = 1; c <= headers.Count; c++)
                worksheet.Cell(row, c).Style.Font.Bold = true;
            row++;
        }

        return row;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~EquipmentReportTests"`
Expected: PASS (8 тестов).

- [ ] **Step 5: Commit**

```powershell
git add Wintime.Control.Infrastructure/Reports/ReportService.cs Wintime.Control.Tests.Integration/Reports/EquipmentReportTests.cs
git commit -m "feat(reports): equipment Excel export with effective status columns"
```

---

### Task 6: Фронт — `REPORT_STATUS`, сериализация `immIds`, метрики отчёта

**Files:**
- Modify: `Wintime-Control-Frontend/src/constants/effectiveStatus.js`
- Modify: `Wintime-Control-Frontend/src/api/reports.js:10-12`
- Create: `Wintime-Control-Frontend/src/utils/equipmentReport.js`
- Modify: `Wintime-Control-Frontend/src/stores/reports.js:21-40`
- Test: `Wintime-Control-Frontend/src/constants/__tests__/effectiveStatus.spec.js` (дополнить)
- Test: `Wintime-Control-Frontend/src/api/__tests__/reports.spec.js`
- Test: `Wintime-Control-Frontend/src/utils/__tests__/equipmentReport.spec.js`

**Interfaces:**
- Consumes: JSON контракт Task 4 (`immData[].seconds`, `.efficiency`, `.totalCycles`, `.days[]`).
- Produces:
  - `NO_DATA_STATUS`, `REPORT_STATUS`, `REPORT_STATUS_KEYS` (7 ключей) из `@/constants/effectiveStatus`;
  - `reportsApi.getEquipment(params)` с `paramsSerializer: { indexes: null }`;
  - из `@/utils/equipmentReport`: `knownSeconds(seconds) → number`, `fleetEfficiency(immData) → number|null`, `countBelow(immData, threshold) → number`, `sumSeconds(immData) → {key: number}`, `toHours(seconds) → string` (1 знак), `formatEfficiency(value) → string`, `efficiencyClass(value) → string`, `buildDayChartOption(days) → echarts option`;
  - геттеры стора `reports`: `totalImms`, `fleetEfficiency`, `problemBelow70`, `problemBelow50`, `totalCycles`.

- [ ] **Step 1: Write the failing tests**

В `effectiveStatus.spec.js` — импорт расширить `REPORT_STATUS, REPORT_STATUS_KEYS` и добавить:

```js
describe('REPORT_STATUS', () => {
  it('= 6 эффективных состояний + NoData, в порядке стека', () => {
    expect(REPORT_STATUS_KEYS).toEqual(
      ['Production', 'Setup', 'Downtime', 'Unplanned', 'NoTask', 'Offline', 'NoData']
    )
    expect(REPORT_STATUS.NoData.label).toBe('Нет данных')
  })

  it('не протекает в EFFECTIVE_STATUS (дашборд)', () => {
    expect(EFFECTIVE_STATUS_KEYS).not.toContain('NoData')
  })
})
```

`src/api/__tests__/reports.spec.js`:

```js
import { describe, it, expect, vi } from 'vitest'
import axios from 'axios'

vi.mock('@/api/client', () => ({
  default: { get: vi.fn(() => Promise.resolve({ data: {} })), post: vi.fn() }
}))

const { default: apiClient } = await import('@/api/client')
const { reportsApi } = await import('@/api/reports')

describe('reportsApi.getEquipment', () => {
  it('сериализует immIds без индексов: immIds=a&immIds=b', () => {
    reportsApi.getEquipment({ dateFrom: '2026-10-01', immIds: ['a', 'b'], archive: 'exclude' })
    const [url, config] = apiClient.get.mock.calls[0]
    const uri = axios.getUri({ url, params: config.params, paramsSerializer: config.paramsSerializer })
    expect(uri).toBe('/reports/equipment?dateFrom=2026-10-01&immIds=a&immIds=b&archive=exclude')
  })
})
```

`src/utils/__tests__/equipmentReport.spec.js`:

```js
import { describe, it, expect } from 'vitest'
import {
  knownSeconds, fleetEfficiency, countBelow, sumSeconds,
  toHours, formatEfficiency, buildDayChartOption
} from '@/utils/equipmentReport'

const H = 3600
const secs = (o) => ({ Production: 0, Setup: 0, Downtime: 0, Unplanned: 0, NoTask: 0, Offline: 0, NoData: 0, ...o })

describe('equipmentReport utils', () => {
  it('knownSeconds исключает Нет связи и Нет данных', () => {
    expect(knownSeconds(secs({ Production: 2 * H, NoTask: H, Offline: 5 * H, NoData: 7 * H }))).toBe(3 * H)
  })

  it('fleetEfficiency взвешенная, а не среднее процентов', () => {
    const immData = [
      { seconds: secs({ Production: 24 * H }), efficiency: 100 },            // 24 ч известно
      { seconds: secs({ Production: 0, NoTask: 1 * H, NoData: 23 * H }), efficiency: 0 } // 1 ч известно
    ]
    // Σ Работа 24 / Σ известного 25 = 96 %, а не (100 + 0) / 2 = 50 %
    expect(fleetEfficiency(immData)).toBe(96)
  })

  it('fleetEfficiency = null, если известного времени нет', () => {
    expect(fleetEfficiency([{ seconds: secs({ NoData: 24 * H }), efficiency: null }])).toBeNull()
    expect(fleetEfficiency([])).toBeNull()
  })

  it('countBelow игнорирует null', () => {
    const immData = [{ efficiency: 40 }, { efficiency: 65 }, { efficiency: 90 }, { efficiency: null }]
    expect(countBelow(immData, 70)).toBe(2)
    expect(countBelow(immData, 50)).toBe(1)
  })

  it('sumSeconds суммирует по ключам', () => {
    const total = sumSeconds([{ seconds: secs({ Setup: H }) }, { seconds: secs({ Setup: 2 * H, Offline: H }) }])
    expect(total.Setup).toBe(3 * H)
    expect(total.Offline).toBe(H)
    expect(total.NoData).toBe(0)
  })

  it('форматирование', () => {
    expect(toHours(5400)).toBe('1.5')
    expect(toHours(undefined)).toBe('0.0')
    expect(formatEfficiency(null)).toBe('—')
    expect(formatEfficiency(84.6)).toBe('85%')
  })

  it('buildDayChartOption: шкала 0–24, 7 серий в стеке, NoData заштрихован', () => {
    const days = [{ date: '2026-10-01T00:00:00Z', seconds: secs({ Production: 20 * H, NoData: 4 * H }) }]
    const opt = buildDayChartOption(days)
    expect(opt.yAxis).toMatchObject({ min: 0, max: 24, interval: 6 })
    expect(opt.xAxis.data).toEqual(['01.10'])
    expect(opt.series).toHaveLength(7)
    expect(opt.series.every(s => s.stack === 'day')).toBe(true)
    expect(opt.series[0].data).toEqual([20])
    const noData = opt.series.find(s => s.name === 'Нет данных')
    expect(noData.itemStyle.decal).toBeTypeOf('object')
    expect(opt.legend.show).toBe(false)
  })
})
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd Wintime-Control-Frontend; npx vitest run src/constants src/api src/utils/__tests__/equipmentReport.spec.js`
Expected: FAIL — `REPORT_STATUS_KEYS` undefined; uri с `immIds[]=`; модуль `@/utils/equipmentReport` не найден.

- [ ] **Step 3: Implement**

`effectiveStatus.js` — добавить в конец файла:

```js
// «Нет данных» — только для отчётов: время без единой записи статуса ТПА (и будущее).
// В EFFECTIVE_STATUS не входит: live-дашборд такого состояния не знает.
export const NO_DATA_STATUS = {
  label: 'Нет данных', bg: 'bg-gray-50', text: 'text-gray-500', dot: 'bg-gray-200', border: 'border-gray-200', hex: '#e5e7eb'
}

// Категории столбца отчёта «Производительность оборудования»; порядок = порядок в стеке.
export const REPORT_STATUS = { ...EFFECTIVE_STATUS, NoData: NO_DATA_STATUS }
export const REPORT_STATUS_KEYS = Object.keys(REPORT_STATUS)
```

`api/reports.js` — `getEquipment`:

```js
  // Отчёт "Производительность оборудования"
  getEquipment(params) {
    return apiClient.get('/reports/equipment', {
      params,
      // ASP.NET List<Guid> ждёт повтор параметра без индексов: immIds=a&immIds=b
      paramsSerializer: { indexes: null },
    })
  },
```

`src/utils/equipmentReport.js`:

```js
import dayjs from 'dayjs'
import { REPORT_STATUS, REPORT_STATUS_KEYS } from '@/constants/effectiveStatus'

// Метрики и представление отчёта «Производительность оборудования».

export function knownSeconds(seconds) {
  const total = REPORT_STATUS_KEYS.reduce((s, k) => s + (seconds?.[k] ?? 0), 0)
  return total - (seconds?.Offline ?? 0) - (seconds?.NoData ?? 0)
}

export function sumSeconds(immData) {
  const total = Object.fromEntries(REPORT_STATUS_KEYS.map(k => [k, 0]))
  for (const item of immData) {
    for (const k of REPORT_STATUS_KEYS) total[k] += item.seconds?.[k] ?? 0
  }
  return total
}

// Взвешенная эффективность парка: Σ Работа / Σ известного времени.
export function fleetEfficiency(immData) {
  const total = sumSeconds(immData)
  const known = knownSeconds(total)
  return known > 0 ? Math.round((total.Production / known) * 100) : null
}

export function countBelow(immData, threshold) {
  return immData.filter(i => i.efficiency != null && i.efficiency < threshold).length
}

export function toHours(seconds) {
  return ((seconds ?? 0) / 3600).toFixed(1)
}

export function formatEfficiency(value) {
  return value == null ? '—' : `${Math.round(value)}%`
}

export function efficiencyClass(value) {
  if (value == null) return 'text-gray-400'
  if (value >= 85) return 'text-green-600'
  if (value >= 70) return 'text-yellow-600'
  return 'text-red-600'
}

const NO_DATA_DECAL = {
  symbol: 'rect', symbolSize: 1, dashArrayX: [1, 0], dashArrayY: [2, 4],
  rotation: Math.PI / 4, color: 'rgba(0, 0, 0, 0.15)'
}

// Опция ECharts для поднёвной диаграммы одного ТПА: столбцы с накоплением, шкала 0–24 ч.
export function buildDayChartOption(days) {
  return {
    legend: { show: false },
    aria: { enabled: true, decal: { show: true } },
    tooltip: {
      trigger: 'axis',
      axisPointer: { type: 'shadow' },
      formatter: (params) => {
        const rows = params
          .filter(p => p.value > 0)
          .map(p => `${p.marker}${p.seriesName}: ${p.value} ч`)
        return [params[0]?.axisValue, ...rows].join('<br/>')
      }
    },
    grid: { left: 36, right: 12, top: 16, bottom: 24 },
    // Дата суток завода приходит как 'YYYY-MM-DDT00:00:00Z' — берём дату без перевода в пояс браузера.
    xAxis: { type: 'category', data: days.map(d => dayjs(String(d.date).slice(0, 10)).format('DD.MM')) },
    yAxis: { type: 'value', min: 0, max: 24, interval: 6, name: 'ч' },
    series: REPORT_STATUS_KEYS.map(k => ({
      name: REPORT_STATUS[k].label,
      type: 'bar',
      stack: 'day',
      barMaxWidth: 40,
      data: days.map(d => +((d.seconds?.[k] ?? 0) / 3600).toFixed(2)),
      itemStyle: { color: REPORT_STATUS[k].hex, decal: k === 'NoData' ? NO_DATA_DECAL : 'none' }
    }))
  }
}
```

`stores/reports.js` — импорт `import { fleetEfficiency, countBelow } from '@/utils/equipmentReport'` и заменить блок `getters` (строки 21-40):

```js
  getters: {
    // Всего ТПА в отчёте оборудования
    totalImms: (state) => state.equipmentReport?.immData?.length ?? 0,

    // Взвешенная эффективность парка (null → «—»)
    fleetEfficiency: (state) => fleetEfficiency(state.equipmentReport?.immData ?? []),

    // Проблемные ТПА: эффективность < 70 % и < 50 % (без «—»)
    problemBelow70: (state) => countBelow(state.equipmentReport?.immData ?? [], 70),
    problemBelow50: (state) => countBelow(state.equipmentReport?.immData ?? [], 50),

    totalCycles: (state) =>
      (state.equipmentReport?.immData ?? []).reduce((sum, i) => sum + i.totalCycles, 0)
  },
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `npx vitest run src/constants src/api src/utils/__tests__/equipmentReport.spec.js`
Expected: PASS.

> Если `decal: 'none'` на обычных сериях вызывает предупреждение ECharts в консоли при ручной проверке (Task 9), убери `decal` у этих серий и оставь `aria.decal.show: true` — проверь визуально, что заштрихован только «Нет данных».

- [ ] **Step 5: Commit**

```powershell
git add src/constants src/api src/utils/equipmentReport.js src/utils/__tests__/equipmentReport.spec.js src/stores/reports.js
git commit -m "feat(reports-ui): report status palette, immIds serialization, fleet metrics"
```

---

### Task 7: Компоненты `EffectiveStatusDayChart` и `EquipmentImmRow`

**Files:**
- Create: `Wintime-Control-Frontend/src/components/reports/EffectiveStatusDayChart.vue`
- Create: `Wintime-Control-Frontend/src/components/reports/EquipmentImmRow.vue`
- Test: `Wintime-Control-Frontend/src/components/reports/__tests__/EquipmentImmRow.spec.js`

**Interfaces:**
- Consumes: `buildDayChartOption`, `toHours`, `formatEfficiency`, `efficiencyClass` (Task 6), `REPORT_STATUS(_KEYS)` (Task 6).
- Produces: `<EffectiveStatusDayChart :days="item.days" />`; `<EquipmentImmRow :item="immDataItem" />` (корень с `data-test="imm-row"`).

- [ ] **Step 1: Write the failing test** `src/components/reports/__tests__/EquipmentImmRow.spec.js`:

```js
// @vitest-environment jsdom
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import EquipmentImmRow from '../EquipmentImmRow.vue'

const H = 3600
const item = (o = {}) => ({
  immId: '1', immName: 'ТПА-1', isActive: true,
  seconds: { Production: 20 * H, Setup: 0, Downtime: 0, Unplanned: 0, NoTask: 0, Offline: 0, NoData: 4 * H },
  totalCycles: 1200, avgCycleSeconds: 35.25, efficiency: 100,
  days: [], ...o
})

function mountRow(props) {
  return mount(EquipmentImmRow, {
    props: { item: props },
    global: { plugins: [ElementPlus], stubs: { EffectiveStatusDayChart: true } }
  })
}

describe('EquipmentImmRow', () => {
  it('показывает имя, эффективность, часы по статусам и циклы', () => {
    const w = mountRow(item())
    expect(w.text()).toContain('ТПА-1')
    expect(w.text()).toContain('100%')
    expect(w.text()).toContain('20.0 ч')
    expect(w.text()).toContain('Нет данных')
    expect(w.text()).toContain('1200')
    expect(w.text()).toContain('35.3 с')
    expect(w.text()).not.toContain('Архив')
  })

  it('архивный ТПА помечен бейджем, эффективность null → «—»', () => {
    const w = mountRow(item({ isActive: false, efficiency: null, avgCycleSeconds: 0 }))
    expect(w.text()).toContain('Архив')
    expect(w.text()).toContain('Эффективность: —')
  })

  it('передаёт days в диаграмму', () => {
    const days = [{ date: '2026-10-01T00:00:00Z', seconds: {} }]
    const w = mountRow(item({ days }))
    expect(w.findComponent({ name: 'EffectiveStatusDayChart' }).props('days')).toEqual(days)
  })
})
```

- [ ] **Step 2: Run test to verify it fails**

Run: `npx vitest run src/components/reports`
Expected: FAIL — `Failed to resolve import "../EquipmentImmRow.vue"`.

- [ ] **Step 3: Implement**

`EffectiveStatusDayChart.vue`:

```vue
<template>
  <div ref="chartRef" class="w-full" style="height: 220px"></div>
</template>

<script setup>
import { ref, onMounted, onUnmounted, watch } from 'vue'
import * as echarts from 'echarts'
import { buildDayChartOption } from '@/utils/equipmentReport'

defineOptions({ name: 'EffectiveStatusDayChart' })

const props = defineProps({
  // [{ date, seconds: { Production, …, NoData } }]
  days: { type: Array, default: () => [] }
})

const chartRef = ref(null)
let chart = null

const render = () => {
  if (!chart) return
  chart.setOption(buildDayChartOption(props.days), true)
}

const onResize = () => chart?.resize()

onMounted(() => {
  chart = echarts.init(chartRef.value)
  render()
  window.addEventListener('resize', onResize)
})

onUnmounted(() => {
  window.removeEventListener('resize', onResize)
  chart?.dispose()
  chart = null
})

watch(() => props.days, render, { deep: true })
</script>
```

`EquipmentImmRow.vue`:

```vue
<template>
  <el-card shadow="never" class="mb-3" data-test="imm-row">
    <template #header>
      <div class="flex items-center justify-between">
        <div class="flex items-center gap-2">
          <span class="font-semibold">{{ item.immName }}</span>
          <el-tag v-if="!item.isActive" size="small" type="info">Архив</el-tag>
        </div>
        <span class="text-sm font-medium" :class="efficiencyClass(item.efficiency)">
          Эффективность: {{ formatEfficiency(item.efficiency) }}
        </span>
      </div>
    </template>

    <div class="flex flex-col lg:flex-row gap-4">
      <div class="flex-1 min-w-0">
        <EffectiveStatusDayChart :days="item.days" />
      </div>

      <div class="lg:w-60 text-sm">
        <div v-for="k in REPORT_STATUS_KEYS" :key="k" class="flex items-center justify-between py-0.5">
          <span class="flex items-center gap-2">
            <span class="inline-block w-2.5 h-2.5 rounded-sm" :style="{ background: REPORT_STATUS[k].hex }"></span>
            {{ REPORT_STATUS[k].label }}
          </span>
          <span class="tabular-nums">{{ toHours(item.seconds?.[k]) }} ч</span>
        </div>
        <div class="border-t mt-2 pt-2 flex justify-between">
          <span>Циклы</span>
          <span class="tabular-nums">{{ item.totalCycles }}</span>
        </div>
        <div class="flex justify-between">
          <span>Ср. цикл</span>
          <span class="tabular-nums">{{ item.avgCycleSeconds > 0 ? `${item.avgCycleSeconds.toFixed(1)} с` : '—' }}</span>
        </div>
      </div>
    </div>
  </el-card>
</template>

<script setup>
import EffectiveStatusDayChart from './EffectiveStatusDayChart.vue'
import { REPORT_STATUS, REPORT_STATUS_KEYS } from '@/constants/effectiveStatus'
import { toHours, formatEfficiency, efficiencyClass } from '@/utils/equipmentReport'

defineProps({
  // элемент immData отчёта «Производительность оборудования»
  item: { type: Object, required: true }
})
</script>
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `npx vitest run src/components/reports`
Expected: PASS (3 теста).

- [ ] **Step 5: Commit**

```powershell
git add src/components/reports/EffectiveStatusDayChart.vue src/components/reports/EquipmentImmRow.vue src/components/reports/__tests__/EquipmentImmRow.spec.js
git commit -m "feat(reports-ui): per-IMM daily effective status chart row"
```

---

### Task 8: Переписать `EquipmentReportView` (фильтры, KPI, список, таблица), удалить `BarChart`

**Files:**
- Rewrite: `Wintime-Control-Frontend/src/views/reports/EquipmentReportView.vue`
- Delete: `Wintime-Control-Frontend/src/components/reports/BarChart.vue`
- Test: `Wintime-Control-Frontend/src/views/reports/__tests__/EquipmentReportView.spec.js`

**Interfaces:**
- Consumes: `immApi.getList()` (`@/api/imm`, без параметров — все ТПА, поле `isActive`), стор `reports` (`loadEquipmentReport`, `exportToExcel`, геттеры Task 6), `EquipmentImmRow` (Task 7), `REPORT_STATUS(_KEYS)`, `toHours`, `formatEfficiency`, `sumSeconds`.
- Produces: страница отчёта; параметры запроса `{ dateFrom, dateTo, immIds: string[], archive: 'exclude'|'include'|'only' }`.

- [ ] **Step 1: Write the failing test** `src/views/reports/__tests__/EquipmentReportView.spec.js`:

```js
// @vitest-environment jsdom
import { describe, it, expect, vi, beforeEach } from 'vitest'
import { mount, flushPromises } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ElementPlus from 'element-plus'

vi.mock('vue-router', () => ({ useRouter: () => ({ push: vi.fn() }) }))
vi.mock('@/api/imm', () => ({ immApi: { getList: vi.fn() } }))
vi.mock('@/api/reports', () => ({ reportsApi: { getEquipment: vi.fn(), exportExcel: vi.fn() } }))

const { immApi } = await import('@/api/imm')
const { reportsApi } = await import('@/api/reports')
const { default: EquipmentReportView } = await import('../EquipmentReportView.vue')

const H = 3600
const row = (id, name, isActive = true) => ({
  immId: id, immName: name, isActive,
  seconds: { Production: 12 * H, Setup: 0, Downtime: 0, Unplanned: 0, NoTask: 12 * H, Offline: 0, NoData: 0 },
  totalCycles: 10, avgCycleSeconds: 30, efficiency: 50, days: []
})

function mountView() {
  return mount(EquipmentReportView, {
    global: {
      plugins: [ElementPlus],
      stubs: { EquipmentImmRow: true, 'el-date-picker': true, 'el-select': true, 'el-option': true }
    }
  })
}

describe('EquipmentReportView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    reportsApi.getEquipment.mockResolvedValue({ data: { immData: [row('1', 'A'), row('2', 'B')] } })
  })

  it('по умолчанию запрашивает все неархивные ТПА и рисует строку на каждый ТПА', async () => {
    immApi.getList.mockResolvedValue({ data: [
      { id: '1', name: 'A', isActive: true },
      { id: '2', name: 'B', isActive: true },
      { id: '3', name: 'X', isActive: false },
    ] })
    const w = mountView()
    await flushPromises()

    const params = reportsApi.getEquipment.mock.calls[0][0]
    expect(params.immIds).toEqual(['1', '2'])
    expect(params.archive).toBe('exclude')
    expect(w.findAllComponents({ name: 'EquipmentImmRow' })).toHaveLength(2)
  })

  it('переключатель архива скрыт, если архивных ТПА нет', async () => {
    immApi.getList.mockResolvedValue({ data: [{ id: '1', name: 'A', isActive: true }] })
    const w = mountView()
    await flushPromises()
    expect(w.find('[data-test="archive-filter"]').exists()).toBe(false)
  })

  it('переключатель архива показан, если архивные ТПА есть', async () => {
    immApi.getList.mockResolvedValue({ data: [
      { id: '1', name: 'A', isActive: true }, { id: '3', name: 'X', isActive: false }
    ] })
    const w = mountView()
    await flushPromises()
    expect(w.find('[data-test="archive-filter"]').exists()).toBe(true)
  })

  it('KPI: взвешенная эффективность парка и две строки проблемных', async () => {
    immApi.getList.mockResolvedValue({ data: [{ id: '1', name: 'A', isActive: true }] })
    const w = mountView()
    await flushPromises()
    const kpi = w.find('[data-test="kpi"]').text()
    expect(kpi).toContain('50%')        // Σ 24 ч Работы / Σ 48 ч известного
    expect(kpi).toContain('< 70 %: 2')
    expect(kpi).toContain('< 50 %: 0')
  })
})
```

> `EquipmentImmRow` стабится по имени файла компонента — Vue выводит имя `EquipmentImmRow` из `<script setup>` SFC. Если `findAllComponents({ name })` не находит стабы, добавь в `EquipmentImmRow.vue` `defineOptions({ name: 'EquipmentImmRow' })`.

- [ ] **Step 2: Run test to verify it fails**

Run: `npx vitest run src/views/reports`
Expected: FAIL — `getEquipment` вызван без `immIds`/`archive`; нет `[data-test="kpi"]`.

- [ ] **Step 3: Implement** — полностью заменить `EquipmentReportView.vue`:

```vue
<template>
  <div>
    <!-- Заголовок -->
    <div class="mb-6 flex items-center justify-between">
      <div>
        <h2 class="text-2xl font-bold text-gray-800">Производительность оборудования</h2>
        <p class="text-gray-600 mt-1">Эффективное состояние каждого ТПА по суткам</p>
      </div>
      <div class="flex gap-2">
        <el-button @click="goBack">Назад</el-button>
        <el-button type="success" @click="exportExcel" :loading="exporting" :disabled="!canLoad">
          <el-icon class="mr-1"><Download /></el-icon>
          Excel
        </el-button>
      </div>
    </div>

    <!-- Фильтры -->
    <el-card class="mb-4">
      <el-form :inline="true">
        <el-form-item label="Период">
          <el-date-picker
            v-model="dateRange"
            type="daterange"
            range-separator="—"
            start-placeholder="Начало"
            end-placeholder="Окончание"
            value-format="YYYY-MM-DD"
            class="w-64"
          />
        </el-form-item>
        <el-form-item v-if="hasArchived" label="Архивные ТПА" data-test="archive-filter">
          <el-radio-group v-model="archiveMode">
            <el-radio-button value="exclude">Нет</el-radio-button>
            <el-radio-button value="include">Да</el-radio-button>
            <el-radio-button value="only">Только</el-radio-button>
          </el-radio-group>
        </el-form-item>
        <el-form-item label="ТПА">
          <el-select
            v-model="selectedImmIds"
            multiple
            collapse-tags
            collapse-tags-tooltip
            filterable
            placeholder="Выберите ТПА"
            class="w-72"
          >
            <el-option
              v-for="imm in immOptions"
              :key="imm.id"
              :label="imm.isActive ? imm.name : `${imm.name} (архив)`"
              :value="imm.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item>
          <el-tooltip :disabled="canLoad" content="Выберите ТПА" placement="top">
            <el-button type="primary" @click="loadReport" :disabled="!canLoad">Сформировать</el-button>
          </el-tooltip>
        </el-form-item>
      </el-form>
    </el-card>

    <!-- Сводные показатели -->
    <div class="grid grid-cols-1 md:grid-cols-4 gap-4 mb-4" data-test="kpi">
      <div class="card">
        <p class="text-sm text-gray-500">Всего ТПА</p>
        <p class="text-2xl font-bold text-gray-800">{{ reportsStore.totalImms }}</p>
      </div>
      <div class="card">
        <p class="text-sm text-gray-500">Эффективность парка</p>
        <p class="text-2xl font-bold" :class="efficiencyClass(reportsStore.fleetEfficiency)">
          {{ formatEfficiency(reportsStore.fleetEfficiency) }}
        </p>
      </div>
      <div class="card">
        <p class="text-sm text-gray-500">Проблемные ТПА</p>
        <p class="text-lg font-bold text-yellow-600">&lt; 70 %: {{ reportsStore.problemBelow70 }}</p>
        <p class="text-lg font-bold text-red-600">&lt; 50 %: {{ reportsStore.problemBelow50 }}</p>
      </div>
      <div class="card">
        <p class="text-sm text-gray-500">Всего циклов</p>
        <p class="text-2xl font-bold text-gray-800">{{ reportsStore.totalCycles }}</p>
      </div>
    </div>

    <!-- Общая легенда -->
    <div class="flex flex-wrap gap-4 mb-3 text-sm text-gray-600">
      <span v-for="k in REPORT_STATUS_KEYS" :key="k" class="flex items-center gap-1.5">
        <span
          class="inline-block w-3 h-3 rounded-sm"
          :class="{ 'no-data-swatch': k === 'NoData' }"
          :style="{ background: REPORT_STATUS[k].hex }"
        ></span>
        {{ REPORT_STATUS[k].label }}
      </span>
    </div>

    <!-- Строки по ТПА -->
    <div v-loading="loading" class="min-h-[120px]">
      <EquipmentImmRow v-for="item in immData" :key="item.immId" :item="item" />
      <el-empty v-if="!loading && immData.length === 0" description="Нет данных за период" />
    </div>

    <!-- Итоговая таблица -->
    <el-card v-if="immData.length > 0" class="mt-4">
      <template #header>
        <span class="font-semibold">
          Итоги за период
          <span v-if="reportData" class="text-gray-500 font-normal ml-2">
            {{ dayjs(reportData.dateFrom).format('DD.MM.YYYY') }} — {{ dayjs(reportData.dateTo).format('DD.MM.YYYY') }}
          </span>
        </span>
      </template>
      <el-table :data="immData" stripe style="width: 100%" :summary-method="getSummaries" show-summary>
        <el-table-column label="ТПА" min-width="150" fixed>
          <template #default="{ row }">
            {{ row.immName }}<span v-if="!row.isActive" class="text-gray-400"> (архив)</span>
          </template>
        </el-table-column>
        <el-table-column
          v-for="k in REPORT_STATUS_KEYS"
          :key="k"
          :label="`${REPORT_STATUS[k].label} (ч)`"
          min-width="110"
          align="right"
        >
          <template #default="{ row }">{{ toHours(row.seconds?.[k]) }}</template>
        </el-table-column>
        <el-table-column prop="totalCycles" label="Циклы" width="90" align="right" />
        <el-table-column label="Ср. цикл (с)" width="110" align="right">
          <template #default="{ row }">{{ row.avgCycleSeconds > 0 ? row.avgCycleSeconds.toFixed(1) : '—' }}</template>
        </el-table-column>
        <el-table-column label="Эффективность" width="130" align="right">
          <template #default="{ row }">
            <span :class="efficiencyClass(row.efficiency)">{{ formatEfficiency(row.efficiency) }}</span>
          </template>
        </el-table-column>
      </el-table>
    </el-card>
  </div>
</template>

<script setup>
import { ref, computed, watch, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import dayjs from 'dayjs'
import { useReportsStore } from '@/stores/reports'
import { immApi } from '@/api/imm'
import EquipmentImmRow from '@/components/reports/EquipmentImmRow.vue'
import { REPORT_STATUS, REPORT_STATUS_KEYS } from '@/constants/effectiveStatus'
import { toHours, formatEfficiency, efficiencyClass, sumSeconds } from '@/utils/equipmentReport'

const router = useRouter()
const reportsStore = useReportsStore()

const loading = ref(false)
const exporting = ref(false)
const dateRange = ref([dayjs().subtract(7, 'day').format('YYYY-MM-DD'), dayjs().format('YYYY-MM-DD')])

// Фильтры не запоминаются между открытиями отчёта.
const allImms = ref([])
const archiveMode = ref('exclude')
const selectedImmIds = ref([])

const hasArchived = computed(() => allImms.value.some(i => !i.isActive))

const immOptions = computed(() => {
  if (archiveMode.value === 'only') return allImms.value.filter(i => !i.isActive)
  if (archiveMode.value === 'include') return allImms.value
  return allImms.value.filter(i => i.isActive)
})

// Смена режима архива — выбор сбрасывается на «все ТПА режима».
watch(archiveMode, () => {
  selectedImmIds.value = immOptions.value.map(i => i.id)
})

const canLoad = computed(() => selectedImmIds.value.length > 0)

const reportData = computed(() => reportsStore.equipmentReport)
const immData = computed(() => reportData.value?.immData ?? [])

const requestParams = () => ({
  dateFrom: dateRange.value[0],
  dateTo: dateRange.value[1],
  immIds: selectedImmIds.value,
  archive: archiveMode.value
})

onMounted(async () => {
  try {
    const { data } = await immApi.getList()
    allImms.value = [...data].sort((a, b) => a.name.localeCompare(b.name))
  } catch {
    ElMessage.error('Ошибка загрузки списка ТПА')
    return
  }
  selectedImmIds.value = immOptions.value.map(i => i.id)
  if (canLoad.value) await loadReport()
})

const loadReport = async () => {
  if (!canLoad.value) return
  loading.value = true
  try {
    await reportsStore.loadEquipmentReport(requestParams())
  } finally {
    loading.value = false
  }
}

const exportExcel = async () => {
  exporting.value = true
  try {
    await reportsStore.exportToExcel('equipment', requestParams())
  } finally {
    exporting.value = false
  }
}

const goBack = () => {
  router.push('/reports')
}

const getSummaries = ({ data }) => {
  const totals = sumSeconds(data)
  return [
    'Итого:',
    ...REPORT_STATUS_KEYS.map(k => toHours(totals[k])),
    data.reduce((sum, row) => sum + row.totalCycles, 0),
    '—',
    formatEfficiency(reportsStore.fleetEfficiency)
  ]
}
</script>

<style scoped>
.card {
  @apply bg-white rounded-lg shadow-md p-4;
}

/* Штриховка «Нет данных» в легенде — как decal на диаграмме */
.no-data-swatch {
  background-image: repeating-linear-gradient(45deg, rgba(0, 0, 0, 0.15) 0 1px, transparent 1px 4px);
}
</style>
```

Удалить старую диаграмму: `git rm src/components/reports/BarChart.vue` и проверить, что ссылок нет: `grep -rn "BarChart" src` → пусто.

- [ ] **Step 4: Run tests to verify they pass**

Run: `npx vitest run src/views/reports`
Expected: PASS (4 теста).

Run: `npx vitest run`
Expected: PASS (весь фронтовый набор, включая `effectiveStatus.spec.js`, `dashboard.spec.js`).

- [ ] **Step 5: Commit**

```powershell
git add -A src/views/reports src/components/reports
git commit -m "feat(reports-ui): equipment report with per-IMM rows, IMM and archive filters"
```

---

### Task 9: Финальная проверка

**Files:** нет изменений кода (только правки, если проверка что-то выявит).

- [ ] **Step 1: Полные наборы тестов**

Run (корень репо): `dotnet test Wintime.Control.Tests.Unit` → Expected: PASS, 0 failed.
Run: `dotnet test Wintime.Control.Tests.Integration` → Expected: PASS, 0 failed.
Run: `cd Wintime-Control-Frontend; npx vitest run` → Expected: PASS, 0 failed.
Run: `npm run build` → Expected: build без ошибок.

- [ ] **Step 2: Ручная проверка в приложении** (скилл `run`): API + `npm run dev`, войти менеджером, открыть «Производительность оборудования», проверить:
  - по строке на каждый ТПА, шкала 0–24 одинаковая у всех;
  - сегодняшний столбец дополнен заштрихованным «Нет данных» до 24 ч, «Нет связи» — сплошной серый;
  - тултип показывает только ненулевые статусы;
  - мультиселект: по умолчанию все; снять всех → «Сформировать» неактивна;
  - переключатель архива: виден только при наличии архивных ТПА, «Только» → бейдж «Архив»;
  - Excel скачивается, колонки и «Итого» соответствуют экрану;
  - дашборд и модалка ТПА (`ImmDetailModal`) — легенда без «Нет данных», таймлайн как раньше.

- [ ] **Step 3: Финальный коммит правок (если были)** и переход к `superpowers:finishing-a-development-branch` (PR в `master`).
