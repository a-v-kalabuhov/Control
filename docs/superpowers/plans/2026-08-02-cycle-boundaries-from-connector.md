> **SUPERSEDED (2026-08-09):** заменено более широким MQTT-контрактом v2 — см.
> `docs/superpowers/specs/2026-08-09-mqtt-contract-v2-design.md`. Этот план не реализовывать.

# Границы цикла измеряет коннектор — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop deriving `ImmCycle` boundaries (`StartTime`/`EndTime`/`DurationSeconds`/`InjectionDurationMs`/`PauseDurationMs`) from MQTT message timestamps; derive them from two new latched sensor types (`cycleStart`, `cycleEnd`, Unix milliseconds) that the connector publishes at `mouldClosed↑`/`mouldOpened↑`. Falls back to the current message-timestamp logic when the sensors are absent or the latch looks stale/inverted.

**Architecture:** `cycleStart`/`cycleEnd` become two new sensor `ParameterType` strings recognized by the same three places that already special-case `cycleCounter` (type validation, numeric storage, COV-threshold enforcement). `CycleProcessingHandler` reads both sensors on cycle-close, validates them against the previous cycle's remembered `cycleEnd`, and derives `StartTime`/`EndTime`/both duration fields from them; `CycleState` gains a `LastCycleEndMs` field to remember the previous cycle's `cycleEnd` across messages. The existing `injectionDuration`/`cyclePause` sensor types (never actually implemented by any connector) are fully replaced — not kept alongside — by `cycleStart`/`cycleEnd`.

**Tech Stack:** ASP.NET Core 9 / EF Core 9 (Npgsql), xUnit + FluentAssertions + NSubstitute for `.NET` tests, in-memory EF provider for handler tests.

## Global Constraints

- No DB schema change: `ImmCycle` columns are unchanged, only their origin changes.
- `injectionDuration` and `cyclePause` are removed from the sensor-type contract everywhere in Control; `cycleStart`/`cycleEnd` are the only new types. No connector in production has ever published the old types (per spec, contract not yet implemented on connector side), so this is a clean replacement, not an additive change.
- `cycleStart`/`cycleEnd` values are Unix **milliseconds** — large enough to overflow `int32` (~13 digits vs. `int.MaxValue` ≈ 2.1×10⁹) — so parsing/validation for these two types must use `long`, not `int`. `cycleCounter` and other existing int-typed sensors are unaffected and stay `int`.
- COV filtering must be forced off (`Threshold = 0`) for `cycleStart`/`cycleEnd`, same mechanism already used for the old duration types in `TemplateCache.Parse`.
- All `DateTime` values written to `ImmCycle.StartTime`/`EndTime` must have `Kind = Utc` (`DateTimeOffset.FromUnixTimeMilliseconds(...).UtcDateTime` satisfies this).
- Fallback path (sensors absent, stale latch, or inverted latch) must reproduce exactly today's behavior for `StartTime`/`EndTime`/`DurationSeconds`, with `InjectionDurationMs`/`PauseDurationMs` left `null`, plus a `LogWarning` for the stale/inverted cases (not for "sensors simply not configured").
- Connector-side implementation is explicitly out of scope for this plan (separate repo, separate work).

---

## File Structure

| File | Change |
|---|---|
| `Wintime.Control.Infrastructure/Handlers/ValidateTelemetryDataHandler.cs` | Replace `injectionDuration`/`cyclePause` cases with `cycleStart`/`cycleEnd` (as `long`-parsed) in `TryValidateSensorValue` and `HasChangedBeyondThreshold`. |
| `Wintime.Control.Infrastructure/Handlers/StoreTelemetryDataHandler.cs` | Replace `injectionDuration`/`cyclePause` cases with `cycleStart`/`cycleEnd` in the numeric-storage `switch`. |
| `Wintime.Control.Infrastructure/Cache/TemplateCache.cs` | Replace `injectionDuration`/`cyclePause` with `cycleStart`/`cycleEnd` in the threshold-zeroing rule. |
| `Wintime.Control.Core/Interfaces/ICycleTracker.cs` | Add `LastCycleEndMs` field to the `CycleState` record. |
| `Wintime.Control.Infrastructure/Handlers/CycleProcessingHandler.cs` | Replace message-timestamp-based boundary computation with sensor-derived computation, guarded by staleness/inversion checks and a fallback path; replace `ReadIntSensor` with `ReadLongSensor`. |
| `Wintime.Control.Core/Entities/ImmCycle.cs` | Update XML doc comments on `InjectionDurationMs`/`PauseDurationMs` to describe the new origin. |
| `Wintime.Control.Tests.Unit/Handlers/ValidateTelemetryDataHandlerTests.cs` | Swap `injectionDuration`/`cyclePause` `InlineData` rows for `cycleStart`/`cycleEnd`; add an overflow-proving case. |
| `Wintime.Control.Tests.Unit/Handlers/StoreTelemetryDataHandlerTests.cs` | Rename/rewrite the two duration-sensor tests for `cycleStart`/`cycleEnd`. |
| `Wintime.Control.Tests.Unit/Cache/TemplateCacheTests.cs` | Swap `injectionDuration`/`cyclePause` theory cases for `cycleStart`/`cycleEnd`. |
| `Wintime.Control.Tests.Unit/Handlers/CycleProcessingHandlerTests.cs` | Replace `MakeCycleContextWithDurations` with `MakeCycleContextWithBoundaries`; rewrite/add tests for derived boundaries, fallback, stale latch, inverted latch, first-cycle-after-restart. |
| `CLAUDE.md` | Rewrite the "Цикл литья и пауза" invariant paragraph's origin sentence. |
| `docs/adr/0010-single-high-downtime-threshold.md` | Fix the forward-reference footnote (still names `injectionDuration`/`cyclePause`) to point at `cycleStart`/`cycleEnd`. |
| `docs/adr/0011-cycle-boundaries-from-connector.md` | New ADR recording this decision, the revoked spec sections, the rejected alternative, and the accepted connector-clock dependency. |
| `docs/superpowers/specs/2026-07-20-pzp-pilot-deploy-methodology-design.md` | Add one bullet to "Риски и открытые вопросы" about the NTP requirement on the connector PC. |

---

### Task 1: `cycleStart`/`cycleEnd` type validation (replaces `injectionDuration`/`cyclePause`)

**Files:**
- Modify: `Wintime.Control.Infrastructure/Handlers/ValidateTelemetryDataHandler.cs:144-154, 274`
- Test: `Wintime.Control.Tests.Unit/Handlers/ValidateTelemetryDataHandlerTests.cs:26-58`

**Interfaces:**
- Consumes: `SensorTemplate.ParameterType` (string), `SensorTemplate.Threshold` (decimal) — existing.
- Produces: `TryValidateSensorValue` now recognizes `"cycleStart"`/`"cycleEnd"` as `long`-parseable types; `HasChangedBeyondThreshold`'s int-family branch recognizes them too (defensive — they should never reach it with nonzero threshold once Task 3 lands, but keep the type dispatch consistent).

- [ ] **Step 1: Write the failing tests**

Edit `ValidateTelemetryDataHandlerTests.cs`: replace the `injectionDuration`/`cyclePause` rows in both `[Theory]` blocks with `cycleStart`/`cycleEnd`, and add one row proving the value must not overflow `int32`.

```csharp
[Theory]
[InlineData("float",        "3.14")]
[InlineData("float",        "-0.5")]
[InlineData("int",          "42")]
[InlineData("int",          "-7")]
[InlineData("boolean",      "true")]
[InlineData("boolean",      "false")]
[InlineData("cycleCounter", "100")]
[InlineData("cycleStart",   "12500")]
[InlineData("cycleEnd",     "3400")]
[InlineData("cycleStart",   "1700000012345")] // 13-значный unix-ms — переполняет int32 (~2.1e9)
[InlineData("string",       "any text")]
public async Task ValidateAsync_ValidSensorValue_SensorPassesThrough(string type, string value)
{
    var immId = Guid.NewGuid();
    var sensor = PipelineTestFixtures.MakeSensor("s1", type, threshold: 0);
    var template = PipelineTestFixtures.MakeTemplate([sensor]);
    var message = PipelineTestFixtures.MakeMessage(immId, new Dictionary<string, string> { ["s1"] = value });
    var context = BuildContext(immId, message, template);
    SetupCacheEntry(immId, context);

    var (success, result) = await _sut.ValidateAsync(context);

    success.Should().BeTrue();
    result.Data!.Sensors.Should().ContainKey("s1");
}

[Theory]
[InlineData("float",        "abc")]
[InlineData("int",          "3.14")]
[InlineData("boolean",      "yes")]
[InlineData("cycleCounter", "one")]
[InlineData("cycleStart",   "12.5")]
[InlineData("cycleEnd",     "abc")]
public async Task ValidateAsync_InvalidSensorValue_SensorRemovedFromResult(string type, string value)
{
    var immId = Guid.NewGuid();
    var sensor = PipelineTestFixtures.MakeSensor("s1", type, threshold: 0, required: false);
    var template = PipelineTestFixtures.MakeTemplate([sensor]);
    var message = PipelineTestFixtures.MakeMessage(immId, new Dictionary<string, string> { ["s1"] = value });
    var context = BuildContext(immId, message, template);
    SetupCacheEntry(immId, context);

    var (success, result) = await _sut.ValidateAsync(context);

    success.Should().BeTrue();
    result.Data!.Sensors.Should().NotContainKey("s1");
}
```

- [ ] **Step 2: Run tests to verify the overflow case fails**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~ValidateTelemetryDataHandlerTests"`
Expected: FAIL — `cycleStart`/`cycleEnd` aren't recognized types yet (`typeValid` falls to `_ => false`), so the `1700000012345` row and the plain `cycleStart`/`cycleEnd` rows fail (`success` still `true` but sensor missing from result).

- [ ] **Step 3: Implement — replace the old duration types with the new boundary types**

In `ValidateTelemetryDataHandler.cs`, `TryValidateSensorValue` (lines 144-154):

```csharp
var typeValid = sensor.ParameterType switch
{
    "string"       => true,
    "float"        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
    "int"          => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
    "boolean"      => bool.TryParse(value, out _),
    "cycleCounter" => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
    "cycleStart"   => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
    "cycleEnd"     => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
    _              => false
};
```

In `HasChangedBeyondThreshold` (line 274), replace the old type names in the int-family branch:

```csharp
else if (sensor.ParameterType is "int" or "cycleCounter")
{
    if (int.TryParse(current, out var cur) && int.TryParse(cached, out var cac))
        return Math.Abs(cur - cac) > (double)sensor.Threshold;
}
else if (sensor.ParameterType is "cycleStart" or "cycleEnd")
{
    if (long.TryParse(current, out var cur) && long.TryParse(cached, out var cac))
        return Math.Abs(cur - cac) > (double)sensor.Threshold;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~ValidateTelemetryDataHandlerTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.Infrastructure/Handlers/ValidateTelemetryDataHandler.cs Wintime.Control.Tests.Unit/Handlers/ValidateTelemetryDataHandlerTests.cs
git commit -m "feat(telemetry): validate cycleStart/cycleEnd as long instead of injectionDuration/cyclePause"
```

---

### Task 2: `cycleStart`/`cycleEnd` numeric storage (replaces `injectionDuration`/`cyclePause`)

**Files:**
- Modify: `Wintime.Control.Infrastructure/Handlers/StoreTelemetryDataHandler.cs:49-57`
- Test: `Wintime.Control.Tests.Unit/Handlers/StoreTelemetryDataHandlerTests.cs:87-122`

**Interfaces:**
- Consumes: `Telemetry.ValueNumeric` is `decimal` — already wide enough for 13-digit ms values, no change needed to the parse target, only to which type strings route into this branch.

- [ ] **Step 1: Write the failing tests**

Replace the two duration-sensor tests in `StoreTelemetryDataHandlerTests.cs` (lines 87-122):

```csharp
/// <summary>
/// Датчик типа <c>cycleStart</c> (защёлкнутый момент смыкания формы, unix-мс)
/// должен сохраняться в <c>ValueNumeric</c> тем же путём, что <c>int</c>.
/// </summary>
[Fact]
public async Task SaveAsync_CycleStartSensor_StoresValueNumeric()
{
    var context = BuildContext(
        sensors: new Dictionary<string, string> { ["cs"] = "1700000012345" },
        templateSensors: [PipelineTestFixtures.MakeSensor("cs", "cycleStart")]);

    await CreateSut().SaveAsync(context);

    var row = await _dbContext.Telemetry.SingleAsync();
    row.ValueNumeric.Should().Be(1700000012345m);
    row.ValueText.Should().BeNull();
}

/// <summary>
/// Датчик типа <c>cycleEnd</c> (защёлкнутый момент раскрытия формы, unix-мс)
/// должен сохраняться в <c>ValueNumeric</c> тем же путём, что <c>int</c>.
/// </summary>
[Fact]
public async Task SaveAsync_CycleEndSensor_StoresValueNumeric()
{
    var context = BuildContext(
        sensors: new Dictionary<string, string> { ["ce"] = "1700000027700" },
        templateSensors: [PipelineTestFixtures.MakeSensor("ce", "cycleEnd")]);

    await CreateSut().SaveAsync(context);

    var row = await _dbContext.Telemetry.SingleAsync();
    row.ValueNumeric.Should().Be(1700000027700m);
    row.ValueText.Should().BeNull();
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~StoreTelemetryDataHandlerTests"`
Expected: FAIL — `cycleStart`/`cycleEnd` fall into the `default` branch and get stored as `ValueText`, so `ValueNumeric` is `null`.

- [ ] **Step 3: Implement**

In `StoreTelemetryDataHandler.cs` (lines 49-57):

```csharp
case "int":
case "cycleCounter":
case "cycleStart":
case "cycleEnd":
    if (decimal.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numI))
        entry.ValueNumeric = numI;
    else
        entry.ValueText = value;
    break;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~StoreTelemetryDataHandlerTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.Infrastructure/Handlers/StoreTelemetryDataHandler.cs Wintime.Control.Tests.Unit/Handlers/StoreTelemetryDataHandlerTests.cs
git commit -m "feat(telemetry): store cycleStart/cycleEnd as numeric instead of injectionDuration/cyclePause"
```

---

### Task 3: Force `Threshold = 0` for `cycleStart`/`cycleEnd` in `TemplateCache`

**Files:**
- Modify: `Wintime.Control.Infrastructure/Cache/TemplateCache.cs:53-58`
- Test: `Wintime.Control.Tests.Unit/Cache/TemplateCacheTests.cs:267-332`

**Interfaces:**
- Consumes: `SensorTemplate(string Name, string ParameterName, string ParameterType, decimal Threshold, ...)` — unchanged.
- Produces: `CachedTemplate.Sensors` entries for `cycleStart`/`cycleEnd` always carry `Threshold == 0m`, regardless of what the JSON config says.

Note: the spec's test list says template validation "отвергает" (rejects) a nonzero threshold. This codebase has no template-upload rejection path today — `TemplateCache.Upsert` never fails, and the existing `injectionDuration`/`cyclePause` precedent (which this replaces) silently *coerces* the threshold to zero rather than raising an error. This plan follows that established pattern for consistency and because it achieves the same functional outcome (COV filter never applies to these sensors) without introducing a new failure-surfacing mechanism that isn't otherwise motivated by the spec.

- [ ] **Step 1: Write the failing tests**

Replace the `injectionDuration`/`cyclePause` theory cases in `TemplateCacheTests.cs` (lines 267-312):

```csharp
/// <summary>
/// Новые семантические типы границ цикла должны разбираться так же,
/// как cycleCounter.
/// </summary>
[Theory]
[InlineData("cycleStart")]
[InlineData("cycleEnd")]
public void Upsert_CycleBoundarySensor_ParsesParameterType(string type)
{
    var cache = new TemplateCache();
    var template = MakeTemplate(json: $$"""
        {
            "sensors": [
                { "name": "s", "field": "s", "type": "{{type}}" }
            ]
        }
        """);

    cache.Upsert(template);

    cache.GetById(template.Id)!.Sensors.Single().ParameterType.Should().Be(type);
}

/// <summary>
/// COV-фильтрация для границ цикла обязана быть выключена: ненулевой
/// порог в конфиге принудительно обнуляется, иначе фильтр (ADR-0005, вариант B)
/// подставит защёлкнутое значение прошлого цикла и границы перестанут двигаться.
/// </summary>
[Theory]
[InlineData("cycleStart")]
[InlineData("cycleEnd")]
public void Upsert_CycleBoundarySensorWithThreshold_ForcesThresholdToZero(string type)
{
    var cache = new TemplateCache();
    var template = MakeTemplate(json: $$"""
        {
            "sensors": [
                { "name": "s", "field": "s", "type": "{{type}}", "threshold": 50 }
            ]
        }
        """);

    cache.Upsert(template);

    cache.GetById(template.Id)!.Sensors.Single().Threshold.Should().Be(0m);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~TemplateCacheTests"`
Expected: FAIL — `Upsert_CycleBoundarySensorWithThreshold_ForcesThresholdToZero` fails because `threshold` stays `50m` (the `if` condition doesn't match `"cycleStart"`/`"cycleEnd"` yet).

- [ ] **Step 3: Implement**

In `TemplateCache.cs` (lines 53-58):

```csharp
// COV-фильтрация для границ цикла обязана быть выключена:
// при ненулевом пороге фильтр подставит защёлкнутое значение прошлого
// цикла (ADR-0005, вариант B) и границы перестанут двигаться — то есть
// потеряется ровно то, ради чего эти сенсоры заведены.
if (type is "cycleStart" or "cycleEnd" && threshold != 0m)
    threshold = 0m;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~TemplateCacheTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.Infrastructure/Cache/TemplateCache.cs Wintime.Control.Tests.Unit/Cache/TemplateCacheTests.cs
git commit -m "feat(templates): force threshold=0 for cycleStart/cycleEnd sensors"
```

---

### Task 4: `CycleState.LastCycleEndMs`

**Files:**
- Modify: `Wintime.Control.Core/Interfaces/ICycleTracker.cs`

**Interfaces:**
- Produces: `CycleState(DateTime? CycleStartTime, int? LastCounterValue, string? LastMode, long? LastCycleEndMs = null)` — the 4th parameter defaults to `null` so every existing positional call site (`new CycleState(x, y, z)`) in production and test code keeps compiling unchanged.

No new test file — this is a data-shape change consumed and exercised entirely by Task 5's tests. `CycleTracker.cs` itself needs no change (it stores `CycleState` generically).

- [ ] **Step 1: Implement**

```csharp
namespace Wintime.Control.Core.Interfaces;

public record CycleState(DateTime? CycleStartTime, int? LastCounterValue, string? LastMode, long? LastCycleEndMs = null);

/// <summary>
/// In-memory хранилище состояния отслеживания циклов по каждому ТПА.
/// </summary>
public interface ICycleTracker
{
    CycleState? Get(Guid immId);
    void Set(Guid immId, CycleState state);
}
```

- [ ] **Step 2: Build to verify no call sites broke**

Run: `dotnet build Wintime.Control.sln`
Expected: Build succeeds — all existing 3-argument `new CycleState(...)` call sites in `CycleProcessingHandler.cs` and `CycleProcessingHandlerTests.cs` still compile because of the default parameter value.

- [ ] **Step 3: Commit**

```bash
git add Wintime.Control.Core/Interfaces/ICycleTracker.cs
git commit -m "feat(cycles): add LastCycleEndMs to CycleState for pause-boundary tracking"
```

---

### Task 5: Derive `ImmCycle` boundaries from `cycleStart`/`cycleEnd`

**Files:**
- Modify: `Wintime.Control.Infrastructure/Handlers/CycleProcessingHandler.cs`
- Test: `Wintime.Control.Tests.Unit/Handlers/CycleProcessingHandlerTests.cs`

**Interfaces:**
- Consumes: `CycleState.LastCycleEndMs` (Task 4), `SensorTemplate.ParameterType == "cycleStart"/"cycleEnd"` (Task 1-3).
- Produces: `ImmCycle.StartTime/EndTime/DurationSeconds/InjectionDurationMs/PauseDurationMs` correctly derived; no change to the public shape of `CycleProcessingHandler` or `CompletedCycle`.

This is the core task. Rules being implemented (from the spec):
- `StartTime = cycleEnd(N−1)` if known, else `cycleStart(N)` (first cycle after restart — no known previous boundary).
- `EndTime = cycleEnd(N)`.
- `InjectionDurationMs = cycleEnd(N) − cycleStart(N)`.
- `PauseDurationMs = cycleStart(N) − cycleEnd(N−1)` if `cycleEnd(N-1)` known, else `null`.
- `DurationSeconds = round((EndTime − StartTime).TotalSeconds)` — unchanged formula, now fed by sensor-derived boundaries.
- Guard before trusting the sensors: `cycleEnd(N) > cycleEnd(N−1)` (skip if no previous value — nothing to compare) and `cycleEnd(N) ≥ cycleStart(N)`. Either failing → fall back to today's message-timestamp logic, with `LogWarning`, and leave both duration fields `null`.
- If either sensor is simply absent from the template → fall back silently (no warning) — this is the "machine without form signals" path, not an error.
- On successful sensor-derived close, remember `cycleEnd(N)` in `CycleState.LastCycleEndMs` for the next cycle. On fallback, leave `LastCycleEndMs` untouched (self-heals once a valid close occurs again).

- [ ] **Step 1: Write the failing tests**

Replace `MakeCycleContextWithDurations` in `CycleProcessingHandlerTests.cs` (lines 60-80) with a boundary-sensor variant:

```csharp
/// <summary>
/// Контекст с тремя сенсорами: счётчик + защёлкнутые границы цикла литья
/// (cycleStart/cycleEnd, unix-мс).
/// </summary>
private static MqttProcessingContext MakeCycleContextWithBoundaries(
    Guid immId, int counter, string mode, long cycleStartMs, long cycleEndMs, DateTime? timestampUtc = null)
{
    var sensors = new[]
    {
        PipelineTestFixtures.MakeSensor("counter", type: "cycleCounter"),
        PipelineTestFixtures.MakeSensor("cs", type: "cycleStart"),
        PipelineTestFixtures.MakeSensor("ce", type: "cycleEnd")
    };
    var template = PipelineTestFixtures.MakeTemplate(sensors);
    var message = PipelineTestFixtures.MakeMessage(immId,
        sensors: new Dictionary<string, string>
        {
            ["counter"] = counter.ToString(),
            ["cs"] = cycleStartMs.ToString(),
            ["ce"] = cycleEndMs.ToString()
        }, mode: mode, timestampUtc: timestampUtc);
    var device = PipelineTestFixtures.MakeImmDto(immId);
    return PipelineTestFixtures.MakeContext("control/imm/x/telemetry", "{}",
        data: message, device: device, template: template);
}
```

Replace `Completed_cycle_stores_injection_duration_and_pause` (lines 170-188) with two tests covering the two-cycle flow (first cycle has no known previous boundary, second cycle does — this is also the spec's key invariant test):

```csharp
[Fact]
public async SystemTask Two_consecutive_cycles_derive_boundaries_from_latched_sensors()
{
    var immId = Guid.NewGuid();
    using var db = CreateDb();
    db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
    await db.SaveChangesAsync();

    var tracker = new Wintime.Control.Infrastructure.Services.CycleTracker();
    var sut = new CycleProcessingHandler(db, tracker, [], NullLogger<CycleProcessingHandler>.Instance);

    const long t0 = 1_700_000_000_000L; // cycleStart(1)
    const long t1 = t0 + 12_300;        // cycleEnd(1)   — InjectionDurationMs(1) = 12300
    const long t2 = t1 + 3_200;         // cycleStart(2) — PauseDurationMs(2) = 3200
    const long t3 = t2 + 12_200;        // cycleEnd(2)   — InjectionDurationMs(2) = 12200

    // Первое сообщение — только сеет трекер (состояние ещё пустое).
    await sut.ProcessAsync(MakeCycleContext(immId, 5, "auto"));
    // Закрывает цикл 1: cycleEnd(0) неизвестен → PauseDurationMs = null,
    // StartTime падает на cycleStart(1) за неимением предыдущей границы.
    await sut.ProcessAsync(MakeCycleContextWithBoundaries(immId, 6, "auto", t0, t1));
    // Закрывает цикл 2: cycleEnd(1) = t1 уже известен трекеру → StartTime(2) = t1,
    // PauseDurationMs(2) = cycleStart(2) - cycleEnd(1).
    await sut.ProcessAsync(MakeCycleContextWithBoundaries(immId, 7, "auto", t2, t3));

    var cycles = await db.ImmCycles.OrderBy(c => c.StartTime).ToListAsync();
    cycles.Should().HaveCount(2);
    var cycle1 = cycles[0];
    var cycle2 = cycles[1];

    cycle1.InjectionDurationMs.Should().Be(12300);
    cycle1.PauseDurationMs.Should().BeNull("cycleEnd(0) неизвестен — первый цикл после рестарта");
    cycle1.StartTime.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(t0).UtcDateTime);
    cycle1.EndTime.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(t1).UtcDateTime);
    cycle1.DurationSeconds.Should().Be(12);

    cycle2.InjectionDurationMs.Should().Be(12200);
    cycle2.PauseDurationMs.Should().Be(3200);
    cycle2.StartTime.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(t1).UtcDateTime, "StartTime = cycleEnd предыдущего цикла");
    cycle2.EndTime.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(t3).UtcDateTime);
    cycle2.DurationSeconds.Should().Be(15);
    cycle2.DurationSeconds.Should().Be(
        (int)Math.Round((cycle2.InjectionDurationMs!.Value + cycle2.PauseDurationMs!.Value) / 1000.0),
        "инвариант: DurationSeconds и сумма длительностей описывают один и тот же отрезок");
}

[Fact]
public async SystemTask Stale_cycle_end_latch_falls_back_to_message_timestamps()
{
    var immId = Guid.NewGuid();
    using var db = CreateDb();
    db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
    await db.SaveChangesAsync();

    var tracker = new Wintime.Control.Infrastructure.Services.CycleTracker();
    var sut = new CycleProcessingHandler(db, tracker, [], NullLogger<CycleProcessingHandler>.Instance);

    const long t0 = 1_700_000_000_000L;
    const long t1 = t0 + 12_300;
    var msg1Time = new DateTime(2024, 1, 1, 12, 0, 1, DateTimeKind.Utc);
    var msg2Time = new DateTime(2024, 1, 1, 12, 0, 2, DateTimeKind.Utc);

    await sut.ProcessAsync(MakeCycleContext(immId, 5, "auto"));
    // Закрывает цикл 1 нормально, LastCycleEndMs трекера становится t1.
    await sut.ProcessAsync(MakeCycleContextWithBoundaries(immId, 6, "auto", t0, t1, msg1Time));
    // cycleEnd(2) совпадает с cycleEnd(1) — защёлка не обновилась.
    await sut.ProcessAsync(MakeCycleContextWithBoundaries(immId, 7, "auto", t1 + 5_000, t1, msg2Time));

    var cycle2 = await db.ImmCycles.OrderBy(c => c.StartTime).Skip(1).SingleAsync();
    cycle2.InjectionDurationMs.Should().BeNull("протухшая защёлка → запасной путь");
    cycle2.PauseDurationMs.Should().BeNull();
    cycle2.StartTime.Should().Be(msg1Time, "запасной путь: старт — метка предыдущего сообщения из трекера");
    cycle2.EndTime.Should().Be(msg2Time, "запасной путь: конец — метка текущего сообщения");
}

[Fact]
public async SystemTask Inverted_cycle_boundaries_fall_back_to_message_timestamps()
{
    var immId = Guid.NewGuid();
    using var db = CreateDb();
    db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
    await db.SaveChangesAsync();

    var tracker = new Wintime.Control.Infrastructure.Services.CycleTracker();
    var sut = new CycleProcessingHandler(db, tracker, [], NullLogger<CycleProcessingHandler>.Instance);

    const long t0 = 1_700_000_000_000L;
    const long t1 = t0 + 12_300;
    var msg1Time = new DateTime(2024, 1, 1, 12, 0, 1, DateTimeKind.Utc);
    var msg2Time = new DateTime(2024, 1, 1, 12, 0, 2, DateTimeKind.Utc);

    await sut.ProcessAsync(MakeCycleContext(immId, 5, "auto"));
    await sut.ProcessAsync(MakeCycleContextWithBoundaries(immId, 6, "auto", t0, t1, msg1Time));
    // cycleEnd(2) < cycleStart(2) — границы переставлены; cycleEnd(2) при этом больше
    // cycleEnd(1), так что срабатывает именно проверка на инверсию, а не на протухание.
    await sut.ProcessAsync(MakeCycleContextWithBoundaries(immId, 7, "auto", t1 + 20_000, t1 + 18_000, msg2Time));

    var cycle2 = await db.ImmCycles.OrderBy(c => c.StartTime).Skip(1).SingleAsync();
    cycle2.InjectionDurationMs.Should().BeNull("переставленные границы → запасной путь");
    cycle2.PauseDurationMs.Should().BeNull();
    cycle2.StartTime.Should().Be(msg1Time);
    cycle2.EndTime.Should().Be(msg2Time);
}
```

Update the comment on the now-unchanged fallback test (`Completed_cycle_without_duration_sensors_leaves_fields_null`, lines 190-208) to reflect the new sensor names:

```csharp
// MakeCycleContext даёт шаблон только со счётчиком — машина без сигналов формы.
```
stays valid as-is (no code change needed in this test — it already only wires up `cycleCounter`).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~CycleProcessingHandlerTests"`
Expected: FAIL — `Two_consecutive_cycles_derive_boundaries_from_latched_sensors` fails because `cycleStart`/`cycleEnd` sensors aren't read at all yet (boundaries still come from message timestamps); the two fallback tests currently "pass" only by accident since fallback is the only path — they'll be exercised meaningfully once Step 3 lands, so confirm at minimum the two-cycle test fails now.

- [ ] **Step 3: Implement**

Replace lines 76-162 of `CycleProcessingHandler.cs` (from `var state = _tracker.Get(immId);` through the end of `ReadIntSensor`) with:

```csharp
        var state = _tracker.Get(immId);

        if (!CycleProcessingPolicy.ShouldProcessCycle(currentMode, taskStatus))
        {
            _tracker.Set(immId, new CycleState(null, currentCounter, currentMode, state?.LastCycleEndMs));
            return;
        }

        if (state is null)
        {
            var startTime = currentMode == ImmMode.Auto ? currentTime : (DateTime?)null;
            _tracker.Set(immId, new CycleState(startTime, currentCounter, currentMode, null));
            return;
        }

        bool cycleWasActive = state.CycleStartTime.HasValue;
        bool counterChanged = state.LastCounterValue.HasValue && state.LastCounterValue.Value != currentCounter;
        bool modeChangedFromAuto = state.LastMode == ImmMode.Auto && currentMode != ImmMode.Auto;
        bool cycleEnded = cycleWasActive && (counterChanged || modeChangedFromAuto);

        long? newLastCycleEndMs = state.LastCycleEndMs;

        if (cycleEnded)
        {
            bool isSuccessful = currentMode != ImmMode.Alarm;
            var fallbackCycleStart = state.CycleStartTime!.Value;
            var cavities = activeTask?.Mold.Cavities ?? 0;

            var cycleStartMs = ReadLongSensor(template, data, "cycleStart");
            var cycleEndMs = ReadLongSensor(template, data, "cycleEnd");
            var prevCycleEndMs = state.LastCycleEndMs;

            bool sensorBoundaryValid = false;
            if (cycleStartMs.HasValue && cycleEndMs.HasValue)
            {
                bool notStale = !prevCycleEndMs.HasValue || cycleEndMs.Value > prevCycleEndMs.Value;
                bool notReversed = cycleEndMs.Value >= cycleStartMs.Value;

                if (notStale && notReversed)
                {
                    sensorBoundaryValid = true;
                }
                else
                {
                    _logger.LogWarning(
                        "IMM {ImmId}: rejected cycleStart/cycleEnd latch (start={Start}, end={End}, prevEnd={PrevEnd}) — falling back to message timestamps",
                        immId, cycleStartMs, cycleEndMs, prevCycleEndMs);
                }
            }

            DateTime cycleStartTime;
            DateTime cycleEndTime;
            int? injectionDurationMs = null;
            int? pauseDurationMs = null;

            if (sensorBoundaryValid)
            {
                cycleEndTime = DateTimeOffset.FromUnixTimeMilliseconds(cycleEndMs!.Value).UtcDateTime;
                cycleStartTime = prevCycleEndMs.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(prevCycleEndMs.Value).UtcDateTime
                    : DateTimeOffset.FromUnixTimeMilliseconds(cycleStartMs!.Value).UtcDateTime;
                injectionDurationMs = (int)(cycleEndMs.Value - cycleStartMs!.Value);
                pauseDurationMs = prevCycleEndMs.HasValue ? (int)(cycleStartMs.Value - prevCycleEndMs.Value) : null;
                newLastCycleEndMs = cycleEndMs.Value;
            }
            else
            {
                cycleStartTime = fallbackCycleStart;
                cycleEndTime = currentTime;
            }

            var duration = (int)Math.Round((cycleEndTime - cycleStartTime).TotalSeconds);

            var cycle = new ImmCycle
            {
                ImmId = immId,
                TaskId = activeTask?.Id,
                MoldId = activeTask?.MoldId,
                StartTime = cycleStartTime,
                EndTime = cycleEndTime,
                DurationSeconds = duration,
                IsSuccessful = isSuccessful,
                Cavities = cavities,
                InjectionDurationMs = injectionDurationMs,
                PauseDurationMs = pauseDurationMs
            };
            _db.ImmCycles.Add(cycle);
            await _db.SaveChangesAsync(ct); // СТАДИЯ 1 — цикл долговечен

            var completed = new CompletedCycle(cycle, activeTask, currentMode, counterChanged);
            foreach (var handler in _handlers) // СТАДИЯ 2
            {
                try
                {
                    await handler.HandleAsync(completed, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Cycle handler {Handler} failed for IMM {ImmId} (cycle {CycleId})",
                        handler.GetType().Name, immId, cycle.Id);
                }
            }

            _logger.LogDebug("IMM {ImmId}: cycle saved — duration {Duration}s, successful={Success}",
                immId, duration, isSuccessful);
        }

        DateTime? newCycleStart = null;
        if (counterChanged && currentMode == ImmMode.Auto)
            newCycleStart = currentTime;
        else if (!cycleWasActive && currentMode == ImmMode.Auto)
            newCycleStart = currentTime;
        else if (cycleWasActive && !cycleEnded)
            newCycleStart = state.CycleStartTime;

        _tracker.Set(immId, new CycleState(newCycleStart, currentCounter, currentMode, newLastCycleEndMs));
    }

    /// <summary>
    /// Прочитать целочисленный (64-битный) сенсор по семантическому типу шаблона.
    /// Возвращает <c>null</c>, если сенсор не описан в шаблоне, отсутствует
    /// в сообщении или значение не парсится.
    /// </summary>
    private static long? ReadLongSensor(CachedTemplate template, MqttTelemetryMessage data, string parameterType)
    {
        var sensor = template.Sensors.FirstOrDefault(s => s.ParameterType == parameterType);
        if (sensor is null)
            return null;
        if (!data.Sensors.TryGetValue(sensor.ParameterName, out var raw))
            return null;
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}
```

Note: `state` used inside the early-return branches (`state?.LastCycleEndMs` and the `state is null` check) is fetched once at the top via `_tracker.Get(immId)`, moved up from its previous position (previously fetched only after the `ShouldProcessCycle` gate). This reordering is behavior-preserving — `Get` is read-only — and is required so `LastCycleEndMs` survives transient policy-gate bounces (e.g. brief `Setup` status) instead of being reset to `null` every time.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~CycleProcessingHandlerTests"`
Expected: PASS — all tests including `SemiAuto_counter_then_idle_counts_output_exactly_once` and `Fractional_cycle_duration_rounds_to_nearest_second` (which don't configure `cycleStart`/`cycleEnd` sensors and must keep exercising the fallback path unchanged).

- [ ] **Step 5: Run the full unit test suite**

Run: `dotnet test Wintime.Control.Tests.Unit`
Expected: PASS — no regressions elsewhere (e.g. `TaskOutputHandler`, other cycle-handler tests that construct `CycleState` positionally).

- [ ] **Step 6: Commit**

```bash
git add Wintime.Control.Infrastructure/Handlers/CycleProcessingHandler.cs Wintime.Control.Tests.Unit/Handlers/CycleProcessingHandlerTests.cs
git commit -m "feat(cycles): derive ImmCycle boundaries from cycleStart/cycleEnd sensors with stale-latch fallback"
```

---

### Task 6: Documentation — `ImmCycle` doc comments, CLAUDE.md, ADR-0010 footnote, new ADR-0011, pilot-deploy note

**Files:**
- Modify: `Wintime.Control.Core/Entities/ImmCycle.cs`
- Modify: `CLAUDE.md`
- Modify: `docs/adr/0010-single-high-downtime-threshold.md`
- Create: `docs/adr/0011-cycle-boundaries-from-connector.md`
- Modify: `docs/superpowers/specs/2026-07-20-pzp-pilot-deploy-methodology-design.md`

No test — documentation-only task, reviewed as a single unit.

- [ ] **Step 1: Update `ImmCycle.cs` XML doc comments**

Find the XML doc comments on `InjectionDurationMs` and `PauseDurationMs` (referencing "Приходит от коннектора сенсором типа injectionDuration" / "Сенсор типа cyclePause") and rewrite them to describe derivation from `cycleStart`/`cycleEnd`:

```csharp
/// <summary>
/// Длительность цикла литья, мс. Вычисляется как cycleEnd(N) − cycleStart(N)
/// по защёлкнутым сенсорам границ формы. <c>null</c>, если сенсоры не
/// сконфигурированы в шаблоне или граница цикла не прошла проверку.
/// </summary>
public int? InjectionDurationMs { get; set; }

/// <summary>
/// Длительность паузы перед циклом, мс. Вычисляется как cycleStart(N) − cycleEnd(N−1).
/// <c>null</c> для первого цикла после рестарта (cycleEnd(N−1) неизвестен) либо
/// если сенсоры границ не сконфигурированы/не прошли проверку.
/// </summary>
public int? PauseDurationMs { get; set; }
```

- [ ] **Step 2: Update `CLAUDE.md`**

Replace the origin sentence in the "Цикл литья и пауза — от коннектора, полный цикл — производный" section:

Old:
```
`ImmCycle.InjectionDurationMs` (смыкание↑ → раскрытие↑) и `ImmCycle.PauseDurationMs`
(пауза перед циклом) приходят сенсорами типов `injectionDuration` / `cyclePause` в
миллисекундах. Полный цикл — их сумма, **не хранится**. У этих типов COV-фильтрация
принудительно выключена (`Threshold = 0`), иначе вариация схлопнется в ровную линию.
```

New:
```
`ImmCycle.StartTime`/`EndTime` и `InjectionDurationMs`/`PauseDurationMs` выводятся из
двух сенсоров-моментов `cycleStart`/`cycleEnd` (unix-мс, защёлкнуты между событиями
формы): `InjectionDurationMs = cycleEnd(N) − cycleStart(N)`, `PauseDurationMs =
cycleStart(N) − cycleEnd(N−1)`. Полный цикл — их сумма, **не хранится**. У этих типов
COV-фильтрация принудительно выключена (`Threshold = 0`), иначе вариация схлопнется в
ровную линию. Без сенсоров в шаблоне — запасной путь: границы по меткам сообщений,
длительности `null`.
```

- [ ] **Step 3: Fix ADR-0010's forward-reference footnote**

In `docs/adr/0010-single-high-downtime-threshold.md`, the "Последствия" bullet (around line 61) still names the now-abandoned fields. This is a stale forward-reference inside a bullet about known future work, not the accepted decision itself (the single-threshold decision is untouched), so it's corrected in place rather than superseded:

Old:
```
переписать детект границ цикла на новые поля `injectionDuration`/`cyclePause` вместо эвристики
```

New:
```
переписать детект границ цикла на новые поля `cycleStart`/`cycleEnd` вместо эвристики
```

- [ ] **Step 4: Write ADR-0011**

Create `docs/adr/0011-cycle-boundaries-from-connector.md`, following the format of `docs/adr/0010-single-high-downtime-threshold.md`:

```markdown
# ADR-0011: Границы цикла литья измеряет коннектор

- **Статус:** Accepted
- **Дата:** 2026-08-02

## Контекст

`CycleProcessingHandler` вычислял `ImmCycle.StartTime`/`EndTime`/`DurationSeconds` как
разность меток двух MQTT-сообщений — тех, между которыми изменился `cycleCounter`.
Метка сообщения — момент, когда коннектор сформировал очередной пакет опроса, а не
момент физического события на машине; между реальной границей цикла и ближайшим
опросом лежит до одного интервала опроса (500 мс по умолчанию) на каждой из двух
границ. Коннектор в это же время измеряет моменты точно (его автомат срабатывает по
фронтам сигналов формы), но раньше вычитал одно из другого и публиковал только
разность (`lastCycleDurationMs`), выбрасывая сами моменты.

`ADR-0010` вводит `injectionDuration`/`cyclePause` как способ получить длительности от
коннектора, но не решает вопрос границ: `StartTime`/`EndTime` оставались по меткам
сообщений, и в одной строке `ImmCycle` соседствовали миллисекундные длительности и
секундные границы без гарантии, что их сумма равна разности границ. На момент
принятия этого решения контракт `injectionDuration`/`cyclePause` не был реализован ни
на одном коннекторе — менять было нечего.

## Решение

Коннектор публикует два новых `ParameterType`: `cycleStart` (момент `mouldClosed↑`) и
`cycleEnd` (момент `mouldOpened↑`), оба — unix-миллисекунды, защёлкнуты между
событиями формы. `injectionDuration`/`cyclePause` из контракта убираются целиком —
заменяются, а не дополняются. Control выводит длительности и границы из этой пары
моментов: `EndTime = cycleEnd(N)`, `StartTime = cycleEnd(N−1)`,
`InjectionDurationMs = cycleEnd(N) − cycleStart(N)`,
`PauseDurationMs = cycleStart(N) − cycleEnd(N−1)`. Тем самым инвариант «сумма
длительностей равна разности границ» выполняется тождественно, а не приближённо.

Это отменяет разделы «Контракт с коннектором» и «Чего перенос не даёт» спеки
`2026-08-01-work-mode-and-two-level-cycle-design.md` (строки 163-170 и 227-231) —
контракт длительностей заменяется контрактом моментов.

## Альтернативы

- **Публиковать и моменты, и длительности одновременно** — отвергнуто: две версии
  одного числа способны разойтись, а правило «какая главная» ничем не обосновано;
  спека work-mode уже запрещала параллельную публикацию двух величин одного смысла.

## Последствия

- Новая зависимость: границы цикла и длительности теперь зависят от синхронизации
  часов ПК коннектора (NTP). Зависимость не новая по своей природе — на тех же часах
  уже строится `MqttTelemetryMessage.Timestamp` и `ImmStatusHistory` — но теперь она
  напрямую влияет на аналитику цикла, а не только на порядок строк. Требование
  зафиксировано в runbook пилотного деплоя, не в коде.
- Запасной путь не убран: коннекторы, которые Control не контролирует (OPC UA, Keba),
  и машины без выведенных сигналов формы продолжают работать по меткам сообщений,
  с `InjectionDurationMs`/`PauseDurationMs = null`. Признак способа измерения не
  вводится отдельным полем — `InjectionDurationMs != null` уже означает «границы
  измерены коннектором».
- Защита от протухшей или переставленной защёлки (`cycleEnd(N) > cycleEnd(N−1)`,
  `cycleEnd(N) ≥ cycleStart(N)`) — при нарушении цикл пишется по запасному пути с
  предупреждением в лог, а не с ошибочными значениями.
- Реализация на стороне коннектора (`Wintime.Connector.UsrModbus`) — отдельная задача
  вне этого репозитория; включение контракта на пилоте должно ждать её готовности.
```

- [ ] **Step 5: Add NTP note to the pilot-deploy spec**

In `docs/superpowers/specs/2026-07-20-pzp-pilot-deploy-methodology-design.md`, under "## Риски и открытые вопросы", add one bullet:

```markdown
- Часы ПК коннектора должны быть синхронизированы (NTP): границы цикла литья
  (`cycleStart`/`cycleEnd`, ADR-0011) и метки телеметрии строятся на локальных часах
  коннектора — уход часов ляжет прямо на аналитику цикла. Проверить синхронизацию
  времени должно войти в чек-лист ввода в строй.
```

- [ ] **Step 6: Commit**

```bash
git add Wintime.Control.Core/Entities/ImmCycle.cs CLAUDE.md docs/adr/0010-single-high-downtime-threshold.md docs/adr/0011-cycle-boundaries-from-connector.md docs/superpowers/specs/2026-07-20-pzp-pilot-deploy-methodology-design.md
git commit -m "docs: ADR-0011 for connector-measured cycle boundaries, update CLAUDE.md invariant and ADR-0010 footnote"
```

---

## Final Verification

- [ ] Run the full solution build: `dotnet build Wintime.Control.sln`
- [ ] Run the full unit test suite: `dotnet test Wintime.Control.Tests.Unit`
- [ ] Run the integration test suite if present: `dotnet test` (whole solution) — confirm no project outside `Wintime.Control.Tests.Unit` references `injectionDuration`/`cyclePause`/`ReadIntSensor`/the old 3-field `CycleState` construction pattern in a way that would break.
- [ ] Grep the solution for any remaining `injectionDuration`/`cyclePause` string literals (`grep -rn "injectionDuration\|cyclePause" --include=*.cs`) — should return nothing outside historical spec/ADR prose.

Connector-side implementation (`Sources/Connectors/Wintime.Connector.UsrModbus`) is a separate, out-of-scope follow-up — this plan only prepares Control to consume the new contract and to keep working via the fallback path until the connector ships it.
