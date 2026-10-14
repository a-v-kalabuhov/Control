# BL-32: прогноз окончания выпуска — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Темп и прогноз окончания выпуска на плитке ТПА считаются на бэкенде по среднему полному периоду цикла (цикл + пауза), без наладки и простоев.

**Architecture:** `CycleProcessingHandler` при создании строки `ImmCycle` пишет паузу перед циклом в
`PauseDurationMs`; решение «значение или `null`» принимает чистая функция `CyclePause.Resolve`.
`GET /api/imm` агрегирует `AVG(DurationSeconds)` и `AVG(PauseDurationMs)` по циклам текущего задания и
отдаёт `ReleaseRatePerHour`/`ReleaseForecastAt`, посчитанные чистой функцией `ReleaseForecast.Calculate`.
`ImmCard.vue` только отображает эти поля.

**Tech Stack:** ASP.NET Core 9, EF Core 9 + Npgsql, xUnit + FluentAssertions (+ EF InMemory в unit), Vue 3 + Vitest + @vue/test-utils.

**Спека:** `docs/superpowers/specs/2026-10-14-bl-32-release-forecast-design.md`

## Global Constraints

- Ветка `feature/bl-32-release-forecast`; master защищён, только PR.
- Миграций нет: `ImmCycle.PauseDurationMs` (`int?`, мс) уже в схеме. Legacy-данных нет — бэкфилл не нужен.
- Порог паузы — `DowntimeSettings.IdleThresholdSeconds` (900 с), из `IOptions<DowntimeSettings>`.
- Пауза = `null`, если: нет предыдущего закрытого цикла ТПА; пауза < 0; пауза ≥ порога; в паузе был статус не из `{Auto, Offline}`.
- Период = `AVG(DurationSeconds) + (AVG(PauseDurationMs) ?? 0) / 1000`, если закрытых циклов задания ≥ 3; иначе `PlannedFullCycleSeconds`.
- Темп = `гнёзда × 3600 / период` шт/ч; прогноз = `now + ⌈(план − факт) / гнёзда⌉ × период`; гнёзда — текущие `Mold.Cavities`.
- Прогноз и темп — только для задания в `InProgress`; статусы задания сравнивать `==`.
- Все `DateTime` в EF-запросах — `Kind=Utc`.
- Коммиты заканчиваются строкой `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

---

### Task 1: Политика паузы `CyclePause`

**Files:**
- Create: `Wintime.Control.Core/Policies/CyclePause.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/CyclePauseTests.cs`

**Interfaces:**
- Produces:
  - `public static int? CyclePause.Resolve(DateTime? previousEndUtc, DateTime startUtc, bool foreignStatusInPause, int thresholdSeconds)` — пауза в мс или `null`.
  - `public static bool CyclePause.IsPauseStatus(string status)` — `true` для `ImmStatus.Auto`/`ImmStatus.Offline`.

- [ ] **Step 1: Написать падающие тесты**

```csharp
using FluentAssertions;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class CyclePauseTests
{
    private static readonly DateTime PrevEnd = new(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc);
    private const int Threshold = 900;

    [Fact]
    public void Resolve_ShortPauseInAuto_ReturnsMilliseconds()
    {
        // залипание формы / ожидание оператора: 2 мин, статус всё время Auto
        CyclePause.Resolve(PrevEnd, PrevEnd.AddMinutes(2), foreignStatusInPause: false, Threshold)
            .Should().Be(120_000);
    }

    [Fact]
    public void Resolve_SubSecondPause_KeepsMilliseconds()
    {
        CyclePause.Resolve(PrevEnd, PrevEnd.AddMilliseconds(1500), false, Threshold).Should().Be(1500);
    }

    [Fact]
    public void Resolve_ZeroPause_ReturnsZero()
    {
        CyclePause.Resolve(PrevEnd, PrevEnd, false, Threshold).Should().Be(0);
    }

    [Fact]
    public void Resolve_NoPreviousCycle_ReturnsNull()
    {
        CyclePause.Resolve(null, PrevEnd, false, Threshold).Should().BeNull();
    }

    [Fact]
    public void Resolve_ForeignStatusInPause_ReturnsNull()
    {
        // короткая ручная наладка (Manual) / простой (Idle) / авария
        CyclePause.Resolve(PrevEnd, PrevEnd.AddMinutes(5), foreignStatusInPause: true, Threshold)
            .Should().BeNull();
    }

    [Fact]
    public void Resolve_PauseAtThreshold_ReturnsNull()
    {
        CyclePause.Resolve(PrevEnd, PrevEnd.AddSeconds(Threshold), false, Threshold).Should().BeNull();
    }

    [Fact]
    public void Resolve_PauseJustBelowThreshold_ReturnsValue()
    {
        CyclePause.Resolve(PrevEnd, PrevEnd.AddSeconds(Threshold - 1), false, Threshold)
            .Should().Be((Threshold - 1) * 1000);
    }

    [Fact]
    public void Resolve_LongOfflineOnly_ReturnsNullByThreshold()
    {
        // долгий обрыв связи: Offline не «чужой» статус, но пауза ≥ порога
        CyclePause.Resolve(PrevEnd, PrevEnd.AddMinutes(45), false, Threshold).Should().BeNull();
    }

    [Fact]
    public void Resolve_NegativePause_ReturnsNull()
    {
        CyclePause.Resolve(PrevEnd, PrevEnd.AddSeconds(-1), false, Threshold).Should().BeNull();
    }

    [Theory]
    [InlineData(ImmStatus.Auto, true)]
    [InlineData(ImmStatus.Offline, true)]
    [InlineData(ImmStatus.Manual, false)]
    [InlineData(ImmStatus.Idle, false)]
    [InlineData(ImmStatus.Alarm, false)]
    public void IsPauseStatus_OnlyAutoAndOffline(string status, bool expected)
    {
        CyclePause.IsPauseStatus(status).Should().Be(expected);
    }
}
```

- [ ] **Step 2: Убедиться, что тесты падают**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~CyclePauseTests"`
Expected: ошибка компиляции — `CyclePause` не существует.

- [ ] **Step 3: Реализация**

```csharp
using Wintime.Control.Core.Constants;

namespace Wintime.Control.Core.Policies;

/// <summary>
/// Пауза перед циклом литья (<c>ImmCycle.PauseDurationMs</c>): от раскрытия формы предыдущего
/// цикла ТПА до смыкания нового. Входит в полный период цикла для прогноза выпуска (ADR-0017).
/// <c>null</c> — пауза не отражает нормальную работу и в средний период не входит: нет
/// предыдущего цикла, в паузе был статус кроме <see cref="ImmStatus.Auto"/>/<see cref="ImmStatus.Offline"/>
/// (наладка, простой, авария) или пауза не короче порога простоя (долгий обрыв связи, граница серии).
/// </summary>
public static class CyclePause
{
    /// <summary>Статусы, которые не прерывают паузу: нормальная работа и кратковременный обрыв связи.</summary>
    public static bool IsPauseStatus(string status) =>
        status == ImmStatus.Auto || status == ImmStatus.Offline;

    public static int? Resolve(DateTime? previousEndUtc, DateTime startUtc,
                               bool foreignStatusInPause, int thresholdSeconds)
    {
        if (previousEndUtc is not DateTime prevEnd || foreignStatusInPause)
            return null;

        var pause = startUtc - prevEnd;
        if (pause < TimeSpan.Zero || pause.TotalSeconds >= thresholdSeconds)
            return null;

        return (int)Math.Round(pause.TotalMilliseconds);
    }
}
```

- [ ] **Step 4: Тесты проходят**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~CyclePauseTests"`
Expected: PASS (14 тестов).

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.Core/Policies/CyclePause.cs Wintime.Control.Tests.Unit/Policies/CyclePauseTests.cs
git commit -m "feat(cycles): CyclePause policy for pause before a cycle (BL-32)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: `CycleProcessingHandler` пишет паузу

**Files:**
- Modify: `Wintime.Control.Infrastructure/Handlers/CycleProcessingHandler.cs` (конструктор; открытие строки ~стр. 69–85; создание на закрытии ~стр. 119–127; новый приватный метод)
- Modify: `Wintime.Control.Core/Entities/ImmCycle.cs:35-41` (doc-комментарий `PauseDurationMs`)
- Test: `Wintime.Control.Tests.Unit/Handlers/CycleProcessingHandlerTests.cs`

**Interfaces:**
- Consumes: `CyclePause.Resolve`, `CyclePause.IsPauseStatus` (Task 1).
- Produces: конструктор `CycleProcessingHandler(ControlDbContext db, ICycleTracker tracker, IEnumerable<ICycleHandler> handlers, ILogger<CycleProcessingHandler> logger, IOptions<DowntimeSettings> downtime)`. DI (`InfrastructureServiceExtensions.cs:66`, `AddScoped`) менять не надо — `IOptions<DowntimeSettings>` уже зарегистрирован в `Program.cs:37`.

- [ ] **Step 1: Обновить существующие вызовы конструктора в тестах**

В `CycleProcessingHandlerTests.cs` заменить во всех вызовах (13 мест)
`NullLogger<CycleProcessingHandler>.Instance)` → `NullLogger<CycleProcessingHandler>.Instance, Downtime)`:

```bash
sed -i 's/NullLogger<CycleProcessingHandler>\.Instance)/NullLogger<CycleProcessingHandler>.Instance, Downtime)/g' Wintime.Control.Tests.Unit/Handlers/CycleProcessingHandlerTests.cs
```

Добавить using-и и поле в класс:

```csharp
using Microsoft.Extensions.Options;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Entities;
using Wintime.Control.Shared.Settings;
```

```csharp
    private static readonly IOptions<DowntimeSettings> Downtime =
        Options.Create(new DowntimeSettings { IdleThresholdSeconds = 900 });
```

Если `using Wintime.Control.Core.Entities;` конфликтует с алиасами `Imm`/`Mold`/`EntityTask` — вместо него писать `Core.Entities.ImmStatusHistory` / `Core.Entities.ImmCycle` полностью.

- [ ] **Step 2: Написать падающие тесты**

Добавить в `CycleProcessingHandlerTests`:

```csharp
    private static readonly DateTime T0 = new(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc);

    /// Полный цикл #1 [T0, T0+12s], затем открытие цикла #2 в момент start2.
    private static async SystemTask RunTwoCyclesAsync(CycleProcessingHandler sut, Guid immId, DateTime start2)
    {
        await sut.ProcessAsync(MakeContext(immId, "auto", currentCycle: new CycleSnapshot(1, T0, null, null)));
        await sut.ProcessAsync(MakeContext(immId, "auto",
            lastCycle: new CompletedCycleSnapshot(1, T0, T0.AddSeconds(12), null, null)));
        await sut.ProcessAsync(MakeContext(immId, "auto", currentCycle: new CycleSnapshot(2, start2, null, null)));
    }

    [Fact]
    public async SystemTask Opened_cycle_gets_pause_after_previous_cycle()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        db.ImmStatusHistory.Add(new ImmStatusHistory { ImmId = immId, Status = ImmStatus.Auto, ChangedAt = T0.AddMinutes(-1) });
        await db.SaveChangesAsync();
        var sut = new CycleProcessingHandler(db, new Wintime.Control.Infrastructure.Services.CycleTracker(), [],
            NullLogger<CycleProcessingHandler>.Instance, Downtime);

        await RunTwoCyclesAsync(sut, immId, start2: T0.AddSeconds(17));

        var first = await db.ImmCycles.SingleAsync(c => c.CycleNumber == 1);
        var second = await db.ImmCycles.SingleAsync(c => c.CycleNumber == 2);
        first.PauseDurationMs.Should().BeNull("предыдущего цикла нет");
        second.PauseDurationMs.Should().Be(5000);
    }

    [Fact]
    public async SystemTask Pause_is_null_when_imm_was_idle_between_cycles()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        db.ImmStatusHistory.AddRange(
            new ImmStatusHistory { ImmId = immId, Status = ImmStatus.Auto, ChangedAt = T0.AddMinutes(-1), EndedAt = T0.AddSeconds(20) },
            new ImmStatusHistory { ImmId = immId, Status = ImmStatus.Idle, ChangedAt = T0.AddSeconds(20), EndedAt = T0.AddSeconds(40) },
            new ImmStatusHistory { ImmId = immId, Status = ImmStatus.Auto, ChangedAt = T0.AddSeconds(40) });
        await db.SaveChangesAsync();
        var sut = new CycleProcessingHandler(db, new Wintime.Control.Infrastructure.Services.CycleTracker(), [],
            NullLogger<CycleProcessingHandler>.Instance, Downtime);

        await RunTwoCyclesAsync(sut, immId, start2: T0.AddSeconds(60));

        (await db.ImmCycles.SingleAsync(c => c.CycleNumber == 2)).PauseDurationMs.Should().BeNull();
    }

    [Fact]
    public async SystemTask Pause_ignores_status_intervals_outside_the_pause()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        db.ImmStatusHistory.AddRange(
            // наладка закончилась до конца цикла #1 — паузу не прерывает
            new ImmStatusHistory { ImmId = immId, Status = ImmStatus.Manual, ChangedAt = T0.AddMinutes(-10), EndedAt = T0.AddSeconds(5) },
            new ImmStatusHistory { ImmId = immId, Status = ImmStatus.Auto, ChangedAt = T0.AddSeconds(5) });
        await db.SaveChangesAsync();
        var sut = new CycleProcessingHandler(db, new Wintime.Control.Infrastructure.Services.CycleTracker(), [],
            NullLogger<CycleProcessingHandler>.Instance, Downtime);

        await RunTwoCyclesAsync(sut, immId, start2: T0.AddSeconds(17));

        (await db.ImmCycles.SingleAsync(c => c.CycleNumber == 2)).PauseDurationMs.Should().Be(5000);
    }

    [Fact]
    public async SystemTask Cycle_created_on_sudden_lastCycle_gets_pause()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        await db.SaveChangesAsync();
        var sut = new CycleProcessingHandler(db, new Wintime.Control.Infrastructure.Services.CycleTracker(), [],
            NullLogger<CycleProcessingHandler>.Instance, Downtime);

        await sut.ProcessAsync(MakeContext(immId, "auto", currentCycle: new CycleSnapshot(1, T0, null, null)));
        await sut.ProcessAsync(MakeContext(immId, "auto",
            lastCycle: new CompletedCycleSnapshot(1, T0, T0.AddSeconds(12), null, null)));
        // currentCycle #2 не видели — сразу lastCycle #2
        await sut.ProcessAsync(MakeContext(immId, "auto",
            lastCycle: new CompletedCycleSnapshot(2, T0.AddSeconds(15), T0.AddSeconds(27), null, null)));

        (await db.ImmCycles.SingleAsync(c => c.CycleNumber == 2)).PauseDurationMs.Should().Be(3000);
    }
```

Если у `CycleSnapshot`/`CompletedCycleSnapshot` позиционные параметры называются иначе — свериться с
`Wintime.Control.Core/DTOs/Mqtt` (`CycleSnapshot(int Number, DateTime StartTime, DateTime? InjectionStartTime, decimal? Cushion)`,
`CompletedCycleSnapshot(int Number, DateTime StartTime, DateTime EndTime, DateTime? InjectionStartTime, decimal? Cushion)`).

- [ ] **Step 3: Убедиться, что тесты падают**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~CycleProcessingHandlerTests"`
Expected: ошибка компиляции — конструктор не принимает 5-й аргумент.

- [ ] **Step 4: Реализация в хендлере**

Using-и:

```csharp
using Microsoft.Extensions.Options;
using Wintime.Control.Shared.Settings;
```

Поле и конструктор:

```csharp
    private readonly ILogger<CycleProcessingHandler> _logger;
    private readonly DowntimeSettings _downtime;

    public CycleProcessingHandler(
        ControlDbContext db,
        ICycleTracker tracker,
        IEnumerable<ICycleHandler> handlers,
        ILogger<CycleProcessingHandler> logger,
        IOptions<DowntimeSettings> downtime)
    {
        _db = db;
        _tracker = tracker;
        _handlers = handlers;
        _logger = logger;
        _downtime = downtime.Value;
    }
```

Открытие строки — внутри `if (existing is null)` перед `new ImmCycle`:

```csharp
                var cavities = activeTask?.Mold.Cavities ?? 0;
                var pauseMs = await ResolvePauseAsync(immId, current.StartTime, ct);
                var opened = new ImmCycle
                {
                    ...
                    Cavities = cavities,
                    PauseDurationMs = pauseMs,
                    IsSuccessful = true // временно — уточняется при закрытии
                };
```

Создание на закрытии — заменить `row ??= new ImmCycle { ... };` на:

```csharp
                var isNewRow = row is null;
                if (row is null)
                {
                    row = new ImmCycle
                    {
                        ImmId = immId,
                        TaskId = activeTask?.Id,
                        MoldId = activeTask?.MoldId,
                        StartTime = last.StartTime,
                        CycleNumber = last.Number,
                        Cavities = activeTask?.Mold.Cavities ?? 0,
                        PauseDurationMs = await ResolvePauseAsync(immId, last.StartTime, ct)
                    };
                }
```

Новый приватный метод в конце класса:

```csharp
    /// <summary>
    /// Пауза перед циклом, начинающимся в <paramref name="startUtc"/> (ADR-0017). Предыдущий —
    /// последний закрытый цикл этого ТПА по <c>StartTime</c> (индекс <c>(ImmId, StartTime)</c>),
    /// независимо от задания; «чужой» статус — интервал истории, пересекающий паузу.
    /// </summary>
    private async Task<int?> ResolvePauseAsync(Guid immId, DateTime startUtc, CancellationToken ct)
    {
        var prevEnd = await _db.ImmCycles
            .Where(c => c.ImmId == immId && c.StartTime < startUtc && c.EndTime != null)
            .OrderByDescending(c => c.StartTime)
            .Select(c => c.EndTime)
            .FirstOrDefaultAsync(ct);
        if (prevEnd is not DateTime prev)
            return null;

        var foreignStatus = await _db.ImmStatusHistory.AnyAsync(h =>
            h.ImmId == immId
            && h.Status != ImmStatus.Auto && h.Status != ImmStatus.Offline // = !CyclePause.IsPauseStatus
            && h.ChangedAt < startUtc
            && (h.EndedAt == null || h.EndedAt > prev), ct);

        return CyclePause.Resolve(prev, startUtc, foreignStatus, _downtime.IdleThresholdSeconds);
    }
```

(`ImmStatus` — `Wintime.Control.Core.Constants`, using уже есть. `Task<int?>` — `System.Threading.Tasks.Task`; в файле есть алиас `SystemTask`, поэтому писать `System.Threading.Tasks.Task<int?>`, если `Task` неоднозначен.)

- [ ] **Step 5: Doc-комментарий `ImmCycle.PauseDurationMs`**

Заменить в `Wintime.Control.Core/Entities/ImmCycle.cs`:

```csharp
    /// <summary>
    /// Пауза ПЕРЕД этим циклом литья, миллисекунды: раскрытие формы предыдущего цикла ТПА →
    /// смыкание этого. Пишет Control при создании строки (<c>CyclePause</c>, ADR-0017);
    /// <c>null</c> — пауза не нормальная работа (нет предыдущего цикла, наладка/простой/авария,
    /// пауза ≥ порога простоя). Полный период цикла = <see cref="DurationSeconds"/> + пауза.
    /// </summary>
    public int? PauseDurationMs { get; set; }
```

- [ ] **Step 6: Тесты проходят**

Run: `dotnet test Wintime.Control.Tests.Unit`
Expected: PASS, все тесты проекта (включая старые тесты хендлера).

- [ ] **Step 7: Commit**

```bash
git add Wintime.Control.Infrastructure/Handlers/CycleProcessingHandler.cs Wintime.Control.Core/Entities/ImmCycle.cs Wintime.Control.Tests.Unit/Handlers/CycleProcessingHandlerTests.cs
git commit -m "feat(cycles): store pause before cycle in ImmCycle.PauseDurationMs (BL-32)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Политика прогноза `ReleaseForecast`

**Files:**
- Create: `Wintime.Control.Core/Policies/ReleaseForecast.cs`
- Test: `Wintime.Control.Tests.Unit/Policies/ReleaseForecastTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record ReleaseForecastResult(decimal? RatePerHour, DateTime? ForecastAt);`
  - `public static ReleaseForecastResult ReleaseForecast.Calculate(DateTime nowUtc, int planQuantity, int actualQuantity, int cavities, int closedCycles, double avgDurationSeconds, double? avgPauseMs, int? plannedFullCycleSeconds)`
  - `public const int ReleaseForecast.MinClosedCycles = 3;`

- [ ] **Step 1: Написать падающие тесты**

```csharp
using FluentAssertions;
using Wintime.Control.Core.Policies;
using Xunit;

namespace Wintime.Control.Tests.Unit.Policies;

public class ReleaseForecastTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Calculate_EnoughCycles_UsesCyclePlusPause()
    {
        // период 20 + 10 = 30 с; темп 2 × 3600 / 30 = 240; остаток 60 шт → 30 смыканий × 30 с
        var r = ReleaseForecast.Calculate(Now, planQuantity: 100, actualQuantity: 40, cavities: 2,
            closedCycles: 3, avgDurationSeconds: 20, avgPauseMs: 10_000, plannedFullCycleSeconds: 60);

        r.RatePerHour.Should().Be(240m);
        r.ForecastAt.Should().Be(Now.AddSeconds(900));
        r.ForecastAt!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Calculate_NoPauses_TreatsPauseAsZero()
    {
        var r = ReleaseForecast.Calculate(Now, 100, 40, 2, closedCycles: 5,
            avgDurationSeconds: 20, avgPauseMs: null, plannedFullCycleSeconds: 60);

        r.RatePerHour.Should().Be(360m);
        r.ForecastAt.Should().Be(Now.AddSeconds(600));
    }

    [Fact]
    public void Calculate_FewCycles_FallsBackToPlannedFullCycle()
    {
        var r = ReleaseForecast.Calculate(Now, 100, 40, 2, closedCycles: 2,
            avgDurationSeconds: 20, avgPauseMs: 10_000, plannedFullCycleSeconds: 60);

        r.RatePerHour.Should().Be(120m);
        r.ForecastAt.Should().Be(Now.AddSeconds(1800));
    }

    [Fact]
    public void Calculate_FewCyclesAndNoPlannedCycle_ReturnsNulls()
    {
        var r = ReleaseForecast.Calculate(Now, 100, 40, 2, closedCycles: 0,
            avgDurationSeconds: 0, avgPauseMs: null, plannedFullCycleSeconds: null);

        r.RatePerHour.Should().BeNull();
        r.ForecastAt.Should().BeNull();
    }

    [Fact]
    public void Calculate_PartialLastShot_RoundsShotsUp()
    {
        // 101 шт / 4 гнезда = 26 смыканий × 60 с
        var r = ReleaseForecast.Calculate(Now, 101, 0, 4, closedCycles: 3,
            avgDurationSeconds: 60, avgPauseMs: null, plannedFullCycleSeconds: null);

        r.ForecastAt.Should().Be(Now.AddSeconds(26 * 60));
    }

    [Fact]
    public void Calculate_PlanDone_KeepsRateHidesForecast()
    {
        var r = ReleaseForecast.Calculate(Now, 100, 100, 2, closedCycles: 50,
            avgDurationSeconds: 20, avgPauseMs: null, plannedFullCycleSeconds: 60);

        r.RatePerHour.Should().Be(360m);
        r.ForecastAt.Should().BeNull();
    }

    [Fact]
    public void Calculate_ZeroCavities_ReturnsNulls()
    {
        var r = ReleaseForecast.Calculate(Now, 100, 40, 0, closedCycles: 5,
            avgDurationSeconds: 20, avgPauseMs: null, plannedFullCycleSeconds: 60);

        r.RatePerHour.Should().BeNull();
        r.ForecastAt.Should().BeNull();
    }

    [Fact]
    public void Calculate_ZeroPeriod_ReturnsNulls()
    {
        var r = ReleaseForecast.Calculate(Now, 100, 40, 2, closedCycles: 5,
            avgDurationSeconds: 0, avgPauseMs: null, plannedFullCycleSeconds: 60);

        r.RatePerHour.Should().BeNull();
        r.ForecastAt.Should().BeNull();
    }

    [Fact]
    public void Calculate_ForecastBeyondMaxDate_ReturnsNullForecast()
    {
        var r = ReleaseForecast.Calculate(DateTime.MaxValue.AddDays(-1), int.MaxValue, 0, 1, closedCycles: 5,
            avgDurationSeconds: 3600, avgPauseMs: null, plannedFullCycleSeconds: null);

        r.RatePerHour.Should().Be(1m);
        r.ForecastAt.Should().BeNull();
    }
}
```

- [ ] **Step 2: Убедиться, что тесты падают**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~ReleaseForecastTests"`
Expected: ошибка компиляции — `ReleaseForecast` не существует.

- [ ] **Step 3: Реализация**

```csharp
namespace Wintime.Control.Core.Policies;

/// <summary>
/// Темп (шт/ч) и прогноз окончания выпуска (UTC). <c>null</c> — не посчитать (нет гнёзд, нет
/// периода). <c>ForecastAt = null</c> также при выполненном плане или выходе за <see cref="DateTime.MaxValue"/>.
/// </summary>
public sealed record ReleaseForecastResult(decimal? RatePerHour, DateTime? ForecastAt);

/// <summary>
/// Прогноз выпуска по среднему полному периоду цикла (ADR-0017): период = средняя длительность
/// закрытых циклов задания + средняя пауза перед циклом (<c>null</c>-паузы — наладка, простои —
/// в среднее не входят). Прошлые остановки в будущее не экстраполируются: прогноз = «если дальше
/// работать без остановок». Пока закрытых циклов меньше <see cref="MinClosedCycles"/> — эталон
/// задания. Гнёзда — текущие <c>Mold.Cavities</c>: это прогноз, а не пересчёт истории.
/// Чистая функция; общая для плитки ТПА и Гантта (PZP-07).
/// </summary>
public static class ReleaseForecast
{
    public const int MinClosedCycles = 3;

    public static ReleaseForecastResult Calculate(DateTime nowUtc, int planQuantity, int actualQuantity,
                                                  int cavities, int closedCycles, double avgDurationSeconds,
                                                  double? avgPauseMs, int? plannedFullCycleSeconds)
    {
        double? period = closedCycles >= MinClosedCycles
            ? avgDurationSeconds + (avgPauseMs ?? 0) / 1000
            : plannedFullCycleSeconds;

        if (cavities <= 0 || period is not double p || p <= 0)
            return new ReleaseForecastResult(null, null);

        var rate = (decimal)(cavities * 3600 / p);

        var remaining = planQuantity - actualQuantity;
        if (remaining <= 0)
            return new ReleaseForecastResult(rate, null);

        long shots = ((long)remaining + cavities - 1) / cavities;
        var seconds = shots * p;
        var maxSeconds = (DateTime.MaxValue - nowUtc).TotalSeconds;
        DateTime? forecast = seconds < maxSeconds ? nowUtc.AddSeconds(seconds) : null;

        return new ReleaseForecastResult(rate, forecast);
    }
}
```

- [ ] **Step 4: Тесты проходят**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~ReleaseForecastTests"`
Expected: PASS (9 тестов). Если `RatePerHour` в тестах на точное равенство расходится из-за double→decimal
(например, 240.00000000000003) — округлять в реализации: `Math.Round((decimal)(cavities * 3600 / p), 2)`.

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.Core/Policies/ReleaseForecast.cs Wintime.Control.Tests.Unit/Policies/ReleaseForecastTests.cs
git commit -m "feat(dashboard): ReleaseForecast policy — rate and release end by mean cycle period (BL-32)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: `GET /api/imm` отдаёт темп и прогноз

**Files:**
- Modify: `Wintime.Control.Core/DTOs/Imm/ImmDto.cs` (два новых поля)
- Modify: `Wintime.Control.API/Controllers/ImmController.cs:101-127` (блок `if (currentTaskIds.Count > 0)`)
- Test: `Wintime.Control.Tests.Integration/Imm/ImmListReleaseForecastTests.cs`

**Interfaces:**
- Consumes: `ReleaseForecast.Calculate`, `ReleaseForecastResult` (Task 3); `ImmCycle.PauseDurationMs` (Task 2).
- Produces: `ImmDto.ReleaseRatePerHour` (`decimal?`), `ImmDto.ReleaseForecastAt` (`DateTime?`, UTC) → JSON `releaseRatePerHour`, `releaseForecastAt` (Task 5).

- [ ] **Step 1: Поля DTO** (в конец `ImmDto`)

```csharp
    /// <summary>
    /// Темп выпуска, шт/ч, по среднему полному периоду цикла задания (<c>ReleaseForecast</c>,
    /// ADR-0017). Только для задания в <c>InProgress</c>; иначе <c>null</c>.
    /// </summary>
    public decimal? ReleaseRatePerHour { get; set; }

    /// <summary>Прогноз окончания выпуска (UTC) — без наладки и простоев. <c>null</c> — не посчитать.</summary>
    public DateTime? ReleaseForecastAt { get; set; }
```

- [ ] **Step 2: Написать падающие интеграционные тесты**

```csharp
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Wintime.Control.Core.DTOs.Imm;
using Wintime.Control.Core.Entities;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Tests.Integration.Infrastructure;
using Xunit;
using TaskStatus = Wintime.Control.Core.Enums.TaskStatus;

namespace Wintime.Control.Tests.Integration.Imm;

/// <summary>BL-32: плитка ТПА — темп и прогноз окончания выпуска по среднему периоду цикла.</summary>
[Collection("Integration")]
public class ImmListReleaseForecastTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;
    public ImmListReleaseForecastTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<ImmDto> GetImmFromListAsync(Guid immId)
    {
        var client = _factory.CreateClient();
        var token = await AuthHelper.GetTokenAsync(client, "test_manager", "Manager123!");
        AuthHelper.SetBearerToken(client, token!);
        var list = await client.GetFromJsonAsync<List<ImmDto>>("/api/imm");
        return list!.Single(i => i.Id == immId);
    }

    /// Задание: план 100, факт 40, 2 гнезда, эталон 60 с; три закрытых цикла по 20 с,
    /// паузы 10 с, null, 10 с (среднее 10 с) и один открытый цикл.
    private async Task<Guid> SeedAsync(TaskStatus status)
    {
        var immId = await _factory.CreateFreshImmAsync();
        var t0 = DateTime.UtcNow.AddMinutes(-10);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ControlDbContext>();
        var mold = new Mold { Name = "M-BL32", FormId = $"FORM-{Guid.NewGuid():N}", Cavities = 2, IsActive = true };
        db.Molds.Add(mold);
        await db.SaveChangesAsync();
        var task = new ShiftTask
        {
            ImmId = immId, MoldId = mold.Id, PlanQuantity = 100, ActualQuantity = 40,
            PlannedFullCycleSeconds = 60, Status = status, SetupStartedAt = t0.AddMinutes(-5),
            StartedAt = status == TaskStatus.InProgress ? t0 : null
        };
        db.ShiftTasks.Add(task);
        await db.SaveChangesAsync();
        int?[] pauses = [10_000, null, 10_000];
        for (var i = 0; i < 3; i++)
        {
            var start = t0.AddSeconds(i * 30);
            db.ImmCycles.Add(new ImmCycle
            {
                ImmId = immId, TaskId = task.Id, MoldId = mold.Id, StartTime = start, EndTime = start.AddSeconds(20),
                DurationSeconds = 20, PauseDurationMs = pauses[i], Cavities = 2, IsSuccessful = true
            });
        }
        db.ImmCycles.Add(new ImmCycle
        {
            ImmId = immId, TaskId = task.Id, MoldId = mold.Id, StartTime = t0.AddSeconds(90), EndTime = null,
            PauseDurationMs = 50_000, Cavities = 2, IsSuccessful = true // открытый — в среднее не входит
        });
        await db.SaveChangesAsync();
        return immId;
    }

    [Fact]
    public async Task InProgress_task_gets_rate_and_forecast_by_mean_cycle_period()
    {
        var immId = await SeedAsync(TaskStatus.InProgress);
        var before = DateTime.UtcNow;

        var dto = await GetImmFromListAsync(immId);

        // период 20 + 10 = 30 с → 2 × 3600 / 30 = 240 шт/ч; остаток 60 шт = 30 смыканий × 30 с = 900 с
        dto.ReleaseRatePerHour.Should().Be(240m);
        dto.ReleaseForecastAt.Should().NotBeNull();
        dto.ReleaseForecastAt!.Value.Should().BeCloseTo(before.AddSeconds(900), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Setup_task_has_no_forecast()
    {
        var immId = await SeedAsync(TaskStatus.Setup);

        var dto = await GetImmFromListAsync(immId);

        dto.ReleaseRatePerHour.Should().BeNull();
        dto.ReleaseForecastAt.Should().BeNull();
    }
}
```

Если `Mold` требует `ProductTypeId`/другие обязательные поля на уровне БД — посмотреть, как создаёт ПФ
`EffectiveStatusHistoryServiceTests.cs:31`, и повторить.

- [ ] **Step 3: Убедиться, что тесты падают**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~ImmListReleaseForecastTests"`
Expected: FAIL — `ReleaseRatePerHour` = null (первый тест); второй может пройти — это нормально.

- [ ] **Step 4: Реализация в контроллере**

Заменить блок `if (currentTaskIds.Count > 0) { ... }` (стр. 101–127) на:

```csharp
        if (currentTaskIds.Count > 0)
        {
            var cycleStats = await _context.ImmCycles
                .Where(c => c.TaskId.HasValue && currentTaskIds.Contains(c.TaskId.Value))
                .GroupBy(c => c.TaskId!.Value)
                .Select(g => new
                {
                    TaskId = g.Key,
                    Count = g.Count(),
                    // Открытые циклы (EndTime = null, DurationSeconds = 0) в среднее не входят
                    ClosedCount = g.Count(c => c.EndTime != null),
                    AvgDuration = g.Where(c => c.EndTime != null)
                        .Average(c => (double?)c.DurationSeconds) ?? 0,
                    // null-паузы (наладка, простой, порог) AVG пропускает — ADR-0017
                    AvgPauseMs = g.Where(c => c.EndTime != null)
                        .Average(c => (double?)c.PauseDurationMs)
                })
                .ToListAsync();

            // Входы прогноза выпуска (BL-32): только задания в работе
            var forecastInputs = await _context.ShiftTasks
                .Where(t => currentTaskIds.Contains(t.Id) && t.Status == Core.Enums.TaskStatus.InProgress)
                .Select(t => new { t.Id, t.PlannedFullCycleSeconds, t.Mold.Cavities })
                .ToDictionaryAsync(t => t.Id);

            var now = DateTime.UtcNow;
            foreach (var dto in imms.Where(d => d.CurrentTaskId.HasValue))
            {
                var taskId = dto.CurrentTaskId!.Value;
                var stats = cycleStats.FirstOrDefault(s => s.TaskId == taskId);
                if (stats != null)
                {
                    dto.CycleCount = stats.Count;
                    dto.AvgCycleTime = (decimal)stats.AvgDuration;
                }

                if (forecastInputs.TryGetValue(taskId, out var input))
                {
                    var forecast = ReleaseForecast.Calculate(now,
                        dto.PlanQuantity ?? 0, dto.ActualQuantity ?? 0, input.Cavities,
                        stats?.ClosedCount ?? 0, stats?.AvgDuration ?? 0, stats?.AvgPauseMs,
                        input.PlannedFullCycleSeconds);
                    dto.ReleaseRatePerHour = forecast.RatePerHour;
                    dto.ReleaseForecastAt = forecast.ForecastAt;
                }
            }
        }
```

`ReleaseForecast` — `Wintime.Control.Core.Policies`; `using` уже есть (контроллер использует `ImmEffectiveStatus`). Проверить, иначе добавить.

- [ ] **Step 5: Тесты проходят**

Run: `dotnet test Wintime.Control.Tests.Integration --filter "FullyQualifiedName~Imm"`
Expected: PASS, включая существующие `ImmListUnplannedCycleCountTests`, `EffectiveStatusTests`.

- [ ] **Step 6: Commit**

```bash
git add Wintime.Control.Core/DTOs/Imm/ImmDto.cs Wintime.Control.API/Controllers/ImmController.cs Wintime.Control.Tests.Integration/Imm/ImmListReleaseForecastTests.cs
git commit -m "feat(dashboard): release rate and forecast in GET /api/imm (BL-32)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Плитка ТПА показывает поля API

**Files:**
- Modify: `Wintime-Control-Frontend/src/components/dashboard/ImmCard.vue:119-143`
- Test: `Wintime-Control-Frontend/src/components/dashboard/__tests__/ImmCard.spec.js` (новый)

**Interfaces:**
- Consumes: `imm.releaseRatePerHour` (число | null), `imm.releaseForecastAt` (ISO-строка UTC | null) — приходят из `GET /api/imm` через spread в `stores/dashboard.js` (`...imm`), правка стора не нужна; `formatForecastMoment(eta, now)` из `@/utils/releaseForecast`.

- [ ] **Step 1: Написать падающий тест**

```js
// @vitest-environment jsdom
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import ElementPlus from 'element-plus'
import ImmCard from '../ImmCard.vue'

const imm = (o = {}) => ({
  id: '1', name: 'ТПА-1', manufacturer: 'Haitian', model: 'MA', status: 'Production',
  currentTaskId: 't1', currentMoldName: 'ПФ-1', personnelName: 'Иванов',
  planQuantity: 100, actualQuantity: 40, cycleCount: 3, currentCycleTime: 20,
  taskStartedAt: new Date(Date.now() - 3_600_000).toISOString(),
  releaseRatePerHour: 240.4, releaseForecastAt: new Date(Date.now() + 900_000).toISOString(),
  lastUpdate: new Date().toISOString(),
  ...o
})

const mountCard = (props) => mount(ImmCard, {
  props: { imm: props },
  global: { plugins: [ElementPlus], stubs: { ImmStatusBadge: true, Document: true, User: true } }
})

describe('ImmCard — темп и прогноз (BL-32)', () => {
  it('темп и прогноз берёт из полей API', () => {
    const w = mountCard(imm())
    expect(w.text()).toContain('240 шт/ч')
    expect(w.text()).toContain('Прогноз окончания')
    expect(w.text()).toMatch(/в \d\d:\d\d/)
  })

  it('нет полей прогноза — темп «—», строка прогноза скрыта', () => {
    const w = mountCard(imm({ releaseRatePerHour: null, releaseForecastAt: null }))
    expect(w.text()).not.toContain('шт/ч')
    expect(w.text()).not.toContain('Прогноз окончания')
  })

  it('не считает темп по прошедшему времени', () => {
    // старый расчёт дал бы 40 шт/ч (40 шт за час)
    const w = mountCard(imm({ releaseRatePerHour: null, releaseForecastAt: null }))
    expect(w.text()).not.toContain('40 шт/ч')
  })
})
```

- [ ] **Step 2: Убедиться, что тест падает**

Run: `cd Wintime-Control-Frontend && npx vitest run src/components/dashboard/__tests__/ImmCard.spec.js`
Expected: FAIL — первый тест видит «40 шт/ч», а не «240 шт/ч».

- [ ] **Step 3: Реализация**

Заменить в `ImmCard.vue` блок от `// Темп производства: ...` до конца `etaLabel` на:

```js
// Темп и прогноз окончания выпуска считает бэкенд по среднему полному периоду цикла
// (цикл + пауза), без наладки и простоев — ADR-0017
const rateLabel = computed(() => {
  const rate = props.imm.releaseRatePerHour
  return rate ? `${Math.round(rate)} шт/ч` : '—'
})

const etaLabel = computed(() => {
  const at = props.imm.releaseForecastAt
  return at ? formatForecastMoment(new Date(at)) : null
})
```

- [ ] **Step 4: Тесты проходят**

Run: `cd Wintime-Control-Frontend && npm test`
Expected: PASS, все тесты фронта (включая `releaseForecast.spec.js`, `dashboard.spec.js`).

- [ ] **Step 5: Commit**

```bash
git add Wintime-Control-Frontend/src/components/dashboard/ImmCard.vue Wintime-Control-Frontend/src/components/dashboard/__tests__/ImmCard.spec.js
git commit -m "feat(dashboard): IMM card shows backend release rate and forecast (BL-32)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: ADR-0017, уточнение ADR-0012, CLAUDE.md, руководство

**Files:**
- Create: `docs/adr/0017-release-forecast-mean-cycle-period.md`
- Modify: `docs/adr/0012-mqtt-contract-v2-cycle-detection.md` (раздел «Последствия», пункт про `PauseDurationMs`)
- Modify: `docs/adr/README.md` (строка в индексе после ADR-0016)
- Modify: `CLAUDE.md` (раздел «Цикл литья …», последнее предложение)
- Modify: `docs/user-guide/manager.md:154-156`

- [ ] **Step 1: ADR-0017**

```markdown
# ADR-0017: Прогноз окончания выпуска по среднему периоду цикла

- **Статус:** Accepted
- **Дата:** 2026-10-14
- **Задача:** BL-32

## Контекст

Плитка ТПА показывала темп `факт / (сейчас − StartedAt)` и прогноз `сейчас + остаток / темп`. В
знаменатель попадали повторная наладка, простои, аварии и время без связи, и прогноз переносил их долю
на весь остаток выпуска. Заказчик (ПЗП) требует, чтобы время наладки в прогноз не входило. Тот же расчёт
нужен Гантту PZP-07 (прогноз завершения выпуска, остаток буфера).

`ImmCycle.DurationSeconds` по контракту v2 — от `MoldClosed` до `MoldOpened`; выталкивание, съём детали и
ожидание оператора (полуавтомат) в него не входят.

## Решение

- **Пауза перед циклом** хранится в `ImmCycle.PauseDurationMs`: `StartTime` нового цикла − `EndTime`
  предыдущего закрытого цикла этого ТПА (независимо от задания). Пишет `CycleProcessingHandler` при
  создании строки. `null`, если нет предыдущего цикла, пауза < 0, пауза ≥ порога простоя
  (`IdleThresholdSeconds`, ADR-0010) или в `ImmStatusHistory` за паузу был статус кроме `Auto`/`Offline`
  (`CyclePause`). Хранится в мс, без экстраполяции.
- **Период** = `AVG(DurationSeconds) + AVG(PauseDurationMs)` по всем закрытым циклам задания
  (`null`-паузы `AVG` пропускает). Меньше 3 закрытых циклов — эталон `PlannedFullCycleSeconds`.
- **Темп** = `гнёзда × 3600 / период`; **прогноз** = `now + ⌈(план − факт) / гнёзда⌉ × период`; гнёзда —
  текущие `Mold.Cavities`. Только для `InProgress`. Не хранится — `ReleaseForecast` (`Core/Policies`),
  в API — `ImmDto.ReleaseRatePerHour`/`ReleaseForecastAt`.
- Прошлые остановки в будущее не экстраполируются: прогноз = «если дальше работать без остановок»;
  непредвиденные остановки поглощает буфер выпуска (PZP-14).

## Альтернативы

- **Темп по прошедшему времени** (как было, или с вычетом интервалов наладки) — переносит прошлые
  простои на остаток; вычет наладки требует истории статусов на каждый запрос.
- **Темп по времени эфф. статуса «Работа»** — то же, что чистая работа, но зависит от точности границ
  статусов.
- **Выборка последних N или 10% циклов** — точность среднего зависит от абсолютного размера выборки, а
  не от доли совокупности; длительность цикла — узкое ядро с тяжёлым правым хвостом (залипания,
  ожидания), малая выборка его пропускает или скачет. `AVG` по всем циклам задания — агрегат в БД по
  индексу, он дешёвый.
- **Только `DurationSeconds` без паузы** — систематически занижает прогноз на долю пауз.
- **Отсекать паузы только порогом** — короткая ручная наладка попадала бы в темп; статус точнее.

## Последствия

- `PauseDurationMs` заполняется (уточнение к ADR-0012: «потребитель» паузы — Control).
- Короткие остановки в `Auto` (залипание формы, ожидание оператора) честно замедляют прогноз.
- Авария в паузе обнуляет её: если ТПА выдаёт аварию при залипании, такие паузы в темп не попадут.
- Пауза между циклами разных заданий хранится по ТПА; трактовка в аналитике — на стороне отчётов.
```

- [ ] **Step 2: Уточнение ADR-0012**

Заменить пункт «Последствий»:

```markdown
- `ImmCycle.PauseDurationMs` в v2 контракте ничем не заполняется (пауза между циклами —
  забота потребителя данных, не Control) — поле осталось в схеме, но пока всегда `null`.
  Кандидат на отдельную задачу, если пауза понадобится напрямую в `ImmCycle`.
```

на:

```markdown
- Пауза между циклами в контракт не входит — коннектор её не вычисляет. *Уточнение
  2026-10-14:* потребитель данных коннектора — Control; он сам вычисляет паузу и хранит её в
  `ImmCycle.PauseDurationMs` (способ — ADR-0017). До BL-32 поле было всегда `null`.
```

- [ ] **Step 3: Индекс ADR** — в `docs/adr/README.md` после строки ADR-0016 добавить:

```markdown
- [ADR-0017](0017-release-forecast-mean-cycle-period.md) — Прогноз окончания выпуска по среднему периоду цикла
```

- [ ] **Step 4: CLAUDE.md** — в разделе «Цикл литья — коннектор детектирует…» заменить последнее предложение
(«`ImmCycle.PauseDurationMs` в v2 контракте ничем не заполняется … пока всегда `null`.») на:

```markdown
`ImmCycle.PauseDurationMs` — пауза перед циклом (от раскрытия предыдущего цикла ТПА), пишет Control
при создании строки (`CyclePause`); `null` — нет предыдущего, пауза ≥ порога простоя или в паузе был
статус кроме `Auto`/`Offline`. Темп и прогноз окончания выпуска на плитке ТПА — `ReleaseForecast`
по среднему полному периоду (`AVG(DurationSeconds) + AVG(PauseDurationMs)` по закрытым циклам
задания, < 3 циклов — эталон), не хранятся; наладка и простои в прогноз не входят. См. ADR-0017.
```

- [ ] **Step 5: Руководство** — в `docs/user-guide/manager.md` заменить строки 154 и 156:

```markdown
- **Темп** — выпуск в штуках в час при работе без остановок: по средней длительности цикла задания
  с учётом обычных пауз между циклами; наладка и простои в темп не входят. В начале выпуска (меньше
  трёх циклов) — по эталону цикла из задания;
```

```markdown
- **Прогноз окончания** — когда будет выполнен план, если ТПА дальше будет работать в текущем темпе
  без остановок. Показывается, пока задание в работе (не в наладке).
```

- [ ] **Step 6: Полная проверка**

Run:
```bash
dotnet build
dotnet test Wintime.Control.Tests.Unit
dotnet test Wintime.Control.Tests.Integration
cd Wintime-Control-Frontend && npm test
```
Expected: сборка без ошибок, все тесты PASS.

- [ ] **Step 7: Commit**

```bash
git add docs/adr/0017-release-forecast-mean-cycle-period.md docs/adr/0012-mqtt-contract-v2-cycle-detection.md docs/adr/README.md CLAUDE.md docs/user-guide/manager.md
git commit -m "docs: ADR-0017 release forecast by mean cycle period; clarify ADR-0012 (BL-32)

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
