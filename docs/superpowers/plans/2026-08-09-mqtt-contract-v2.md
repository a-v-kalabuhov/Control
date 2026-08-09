# MQTT-контракт v2 (Control-сторона) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Перевести Control на MQTT-контракт v2: структурированные `currentCycle`/`lastCycle` от коннектора вместо детекции цикла по `cycleCounter`/меткам сообщений; `sensors` со флагом ошибки чтения; открытие/закрытие одной строки `ImmCycle` вместо разового создания при закрытии.

**Architecture:** `DecodeTelemetryDataHandler` парсит новую форму payload в `MqttTelemetryMessage` с `Dictionary<string, SignalValue>` и nullable `CurrentCycle`/`LastCycle`. `ValidateTelemetryDataHandler` отбрасывает `error=true` сигналы и сокращённый `ParameterType`-свитч. `CycleProcessingHandler` открывает `ImmCycle` (EndTime=null) по `currentCycle`, закрывает по `lastCycle`; идентичность цикла — составной ключ `(CycleNumber, StartTime)`, с ЗА-проверкой в БД перед созданием строки (переживает рестарт Control). Даунстрим-потребители `ImmCycle.EndTime` (теперь nullable) получают точечные фильтры/фиксы там, где открытые циклы искажали бы агрегаты длительности — кроме агрегата остатка ресурса формы, который должен их учитывать (это и есть цель хранения открытых циклов).

**Tech Stack:** ASP.NET Core 9 / EF Core 9 (Npgsql), xUnit + FluentAssertions + NSubstitute, in-memory EF provider для unit-тестов хендлеров.

## Global Constraints

- Контракт v1 не поддерживается параллельно — сообщения без `sensors`/`currentCycle`/`lastCycle` в JSON отклоняются на decode (осознанный breaking change).
- Все `DateTime`, записываемые в БД, обязаны иметь `Kind=Utc` (см. CLAUDE.md) — парсинг timestamp'ов переиспользует существующий `TryParseTimestamp`, который уже это гарантирует.
- Идентичность цикла — всегда пара `(CycleNumber, StartTime)`, никогда один `CycleNumber` (см. спеку, раздел B/D) — счётчик коннектора легитимно обнуляется без разрыва связи.
- Открытые циклы (`EndTime=null`) обязаны учитываться в агрегатах остатка ресурса формы (`allTimeCycleStats` в `ReportService`), но исключаться из агрегатов длительности/среднего цикла (`DurationSeconds` ещё не заполнен на открытой строке).
- `IsSuccessful` определяется в момент ЗАКРЫТИЯ строки (`currentMode != ImmMode.Alarm` из сообщения с `lastCycle`), не в момент открытия — на открытии ставится временное `true` (совпадает с сегодняшним поведением, где успех известен только к моменту завершения).
- Каждая задача заканчивается зелёным прогоном `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~<Handler>Tests"` и коммитом.

---

## File Structure

| Файл | Изменение |
|---|---|
| `Wintime.Control.Core/DTOs/Mqtt/SignalValue.cs` | Создать: `record SignalValue(string Value, bool Error)`. |
| `Wintime.Control.Core/DTOs/Mqtt/CycleSnapshot.cs` | Создать: `record CycleSnapshot(int Number, DateTime StartTime, DateTime? InjectionStartTime, decimal? Cushion)`. |
| `Wintime.Control.Core/DTOs/Mqtt/CompletedCycleSnapshot.cs` | Создать: `record CompletedCycleSnapshot(int Number, DateTime StartTime, DateTime EndTime, DateTime? InjectionStartTime, decimal? Cushion)`. |
| `Wintime.Control.Core/DTOs/Mqtt/MqttTelemetryMessage.cs` | `Sensors` меняет тип на `Dictionary<string, SignalValue>`; добавить `CurrentCycle`/`LastCycle`. |
| `Wintime.Control.Core/Entities/ImmCycle.cs` | `EndTime` → nullable; добавить `Cushion`, `InjectionStartTime`, `CycleNumber`. |
| `Wintime.Control.Infrastructure/Data/ControlDbContext.cs` | Настроить новые колонки + уникальный частичный индекс `(ImmId, CycleNumber, StartTime)`. |
| `Wintime.Control.Infrastructure/Migrations/*` | Новая EF-миграция. |
| `Wintime.Control.Infrastructure/Handlers/DecodeTelemetryDataHandler.cs` | Парсинг новой формы `sensors`, `currentCycle`, `lastCycle`. |
| `Wintime.Control.Infrastructure/Handlers/ValidateTelemetryDataHandler.cs` | Сокращённый `ParameterType`, отбрасывание `error=true`, `cycleCounter` как зарезервированное имя. |
| `Wintime.Control.Infrastructure/Handlers/StoreTelemetryDataHandler.cs` | Чтение `SignalValue.Value`, `cycleCounter` без записи в `Template`. |
| `Wintime.Control.Infrastructure/Cache/TemplateCache.cs` | Убрать спец-случай `injectionDuration`/`cyclePause` (типы больше не существуют). |
| `Wintime.Control.Core/Interfaces/ICycleTracker.cs` | `CycleState` → `(int? OpenCycleNumber, DateTime? OpenCycleStartTime, Guid? OpenCycleId)`. |
| `Wintime.Control.Infrastructure/Handlers/CycleProcessingHandler.cs` | Полная замена логики детекции на открытие/закрытие по `currentCycle`/`lastCycle`. |
| `Wintime.Control.Infrastructure/Handlers/UnplannedRunHandler.cs` | `completed.Cycle.EndTime` → `.Value` (гарантированно не null в момент вызова Stage 2). |
| `Wintime.Control.Infrastructure/Reports/ReportService.cs` | Фильтр `EndTime != null` на 4 запросах длительности; НЕ трогать 2 запроса остатка ресурса. |
| `Wintime.Control.API/Controllers/ImmController.cs` | Фильтр `EndTime != null` на подсчёте сирот-циклов; `TelemetryCycleDto.End` → nullable. |
| `Wintime.Control.API/Controllers/UnplannedRunController.cs` | Фильтр `EndTime != null` на агрегатах эпизода; `.Value` фикс в `GetCandidates`. |
| Тестовые файлы — см. по задачам. |

---

### Task 1: DTO-контракт — `SignalValue`, `CycleSnapshot`, `CompletedCycleSnapshot`, `MqttTelemetryMessage`

**Files:**
- Create: `Wintime.Control.Core/DTOs/Mqtt/SignalValue.cs`
- Create: `Wintime.Control.Core/DTOs/Mqtt/CycleSnapshot.cs`
- Create: `Wintime.Control.Core/DTOs/Mqtt/CompletedCycleSnapshot.cs`
- Modify: `Wintime.Control.Core/DTOs/Mqtt/MqttTelemetryMessage.cs`

**Interfaces:**
- Produces: `SignalValue(string Value, bool Error)`, `CycleSnapshot(int Number, DateTime StartTime, DateTime? InjectionStartTime, decimal? Cushion)`, `CompletedCycleSnapshot(int Number, DateTime StartTime, DateTime EndTime, DateTime? InjectionStartTime, decimal? Cushion)` — используются во всех последующих задачах.
- Produces: `MqttTelemetryMessage.Sensors : Dictionary<string, SignalValue>`, `MqttTelemetryMessage.CurrentCycle : CycleSnapshot?`, `MqttTelemetryMessage.LastCycle : CompletedCycleSnapshot?`.

Чистый data-shape change, без логики — тестов не требует (тестируется через Task 2 по потребителям). Компиляция всего решения после этой задачи ожидаемо ломается (Decode/Validate/Store ссылаются на старый `Dictionary<string,string>`) — это устраняется в Tasks 2-4, которые идут сразу следом в одной сессии.

- [ ] **Step 1: Создать `SignalValue.cs`**

```csharp
namespace Wintime.Control.Core.DTOs.Mqtt;

public sealed record SignalValue(string Value, bool Error);
```

- [ ] **Step 2: Создать `CycleSnapshot.cs`**

```csharp
namespace Wintime.Control.Core.DTOs.Mqtt;

public sealed record CycleSnapshot(int Number, DateTime StartTime, DateTime? InjectionStartTime, decimal? Cushion);
```

- [ ] **Step 3: Создать `CompletedCycleSnapshot.cs`**

```csharp
namespace Wintime.Control.Core.DTOs.Mqtt;

public sealed record CompletedCycleSnapshot(
    int Number, DateTime StartTime, DateTime EndTime, DateTime? InjectionStartTime, decimal? Cushion);
```

- [ ] **Step 4: Обновить `MqttTelemetryMessage.cs`**

```csharp
using System.Text.Json.Serialization;

namespace Wintime.Control.Core.DTOs.Mqtt;

public class MqttTelemetryMessage
{
    /// <summary>
    /// Момент формирования сообщения продюсером. Всегда UTC (<see cref="DateTimeKind.Utc"/>):
    /// значение уходит в колонку <c>timestamptz</c>, а <c>Kind=Unspecified</c> там даёт
    /// исключение Npgsql. Точность — как пришла от продюсера, собственного округления нет.
    /// Единственный производитель значения — <c>DecodeTelemetryDataHandler</c>.
    /// </summary>
    public DateTime TimestampUtc { get; set; }
    public string DeviceId { get; set; } = string.Empty;
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }
    public Dictionary<string, SignalValue> Sensors { get; set; } = [];
    public CycleSnapshot? CurrentCycle { get; set; }
    public CompletedCycleSnapshot? LastCycle { get; set; }
}
```

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.Core/DTOs/Mqtt/SignalValue.cs Wintime.Control.Core/DTOs/Mqtt/CycleSnapshot.cs Wintime.Control.Core/DTOs/Mqtt/CompletedCycleSnapshot.cs Wintime.Control.Core/DTOs/Mqtt/MqttTelemetryMessage.cs
git commit -m "feat(mqtt): add contract v2 DTOs (SignalValue, CycleSnapshot, CompletedCycleSnapshot)"
```

---

### Task 2: `DecodeTelemetryDataHandler` — парсинг v2 payload

**Files:**
- Modify: `Wintime.Control.Infrastructure/Handlers/DecodeTelemetryDataHandler.cs`
- Test: `Wintime.Control.Tests.Unit/Handlers/DecodeTelemetryDataHandlerTests.cs` (создать, если не существует — проверить перед началом)
- Test: `Wintime.Control.Tests.Unit/Helpers/PipelineTestFixtures.cs`

**Interfaces:**
- Consumes: `SignalValue`, `CycleSnapshot`, `CompletedCycleSnapshot` (Task 1).
- Produces: `DecodeAsync` возвращает `MqttTelemetryMessage` с заполненными `Sensors`/`CurrentCycle`/`LastCycle`, либо `(false, context)` при отсутствии/битости обязательных секций.

- [ ] **Step 1: Проверить существующий тестовый файл**

```bash
ls Wintime.Control.Tests.Unit/Handlers/DecodeTelemetryDataHandlerTests.cs 2>/dev/null || echo "not found"
```

Если файл существует — прочитать его целиком перед правкой (сохранить существующие проверки топика/device-lookup, которые не меняются этим контрактом). Если не существует — создать с нуля по шагу 2.

- [ ] **Step 2: Обновить фикстуру `MakeMessage` под новый `Sensors`-тип**

В `PipelineTestFixtures.cs` заменить:

```csharp
public static MqttTelemetryMessage MakeMessage(
    Guid immId,
    Dictionary<string, string>? sensors = null,
    string? mode = "auto",
    DateTime? timestampUtc = null)
    => new()
    {
        TimestampUtc = timestampUtc ?? DateTime.UtcNow,
        DeviceId = immId.ToString(),
        Mode = mode,
        Sensors = sensors ?? []
    };
```

на:

```csharp
public static MqttTelemetryMessage MakeMessage(
    Guid immId,
    Dictionary<string, SignalValue>? sensors = null,
    string? mode = "auto",
    DateTime? timestampUtc = null,
    CycleSnapshot? currentCycle = null,
    CompletedCycleSnapshot? lastCycle = null)
    => new()
    {
        TimestampUtc = timestampUtc ?? DateTime.UtcNow,
        DeviceId = immId.ToString(),
        Mode = mode,
        Sensors = sensors ?? [],
        CurrentCycle = currentCycle,
        LastCycle = lastCycle
    };

public static Dictionary<string, SignalValue> MakeSensors(params (string Name, string Value)[] values)
{
    var dict = new Dictionary<string, SignalValue>();
    foreach (var (name, value) in values)
        dict[name] = new SignalValue(value, Error: false);
    if (!dict.ContainsKey("cycleCounter"))
        dict["cycleCounter"] = new SignalValue("0", Error: false);
    return dict;
}
```

`MakeSensors` всегда добавляет `cycleCounter`, если вызывающий его не указал явно — большинство тестов ниже по пайплайну не заботятся о нём, но контракт v2 требует его присутствия на decode.

- [ ] **Step 3: Написать падающие тесты**

Создать/дополнить `Wintime.Control.Tests.Unit/Handlers/DecodeTelemetryDataHandlerTests.cs`:

```csharp
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Wintime.Control.Core.Cache;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Infrastructure.Handlers;
using NSubstitute;
using Task = System.Threading.Tasks.Task;

namespace Wintime.Control.Tests.Unit.Handlers;

public class DecodeTelemetryDataHandlerTests
{
    private static ControlDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ControlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static DecodeTelemetryDataHandler CreateSut(ControlDbContext db, ITemplateCache cache)
        => new(db, cache, NullLogger<DecodeTelemetryDataHandler>.Instance);

    private static async Task<Guid> SeedImmAsync(ControlDbContext db, ITemplateCache cache)
    {
        var templateId = Guid.NewGuid();
        var immId = Guid.NewGuid();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", TemplateId = templateId, IsActive = true });
        await db.SaveChangesAsync();
        cache.GetById(templateId).Returns(new CachedTemplate(templateId, "T", DateTime.UtcNow, 60, []));
        return immId;
    }

    [Fact]
    public async Task DecodeAsync_FullV2Payload_ParsesSensorsAndBothCycleBlocks()
    {
        using var db = CreateDb();
        var cache = Substitute.For<ITemplateCache>();
        var immId = await SeedImmAsync(db, cache);
        var payload = $$"""
            {
              "timestamp": "2026-08-09T12:34:56.789Z",
              "mode": "auto",
              "sensors": {
                "cycleCounter": { "value": "42", "error": false },
                "matTemp1": { "value": "215.3", "error": false }
              },
              "currentCycle": {
                "number": 42,
                "startTime": "2026-08-09T12:34:50.000Z",
                "injectionStartTime": "2026-08-09T12:34:50.800Z",
                "cushion": 5.2
              },
              "lastCycle": {
                "number": 41,
                "startTime": "2026-08-09T12:34:35.000Z",
                "endTime": "2026-08-09T12:34:50.000Z",
                "injectionStartTime": "2026-08-09T12:34:35.800Z",
                "cushion": 5.1
              }
            }
            """;
        var sut = CreateSut(db, cache);

        var (success, updated) = await sut.DecodeAsync(new MqttProcessingContext(
            Guid.NewGuid(), $"control/imm/{immId}/telemetry", payload, null, null, null));

        success.Should().BeTrue();
        updated.Data!.Sensors["cycleCounter"].Should().Be(new SignalValue("42", false));
        updated.Data.Sensors["matTemp1"].Should().Be(new SignalValue("215.3", false));
        updated.Data.CurrentCycle.Should().Be(new CycleSnapshot(42,
            new DateTime(2026, 8, 9, 12, 34, 50, DateTimeKind.Utc), new DateTime(2026, 8, 9, 12, 34, 50, 800, DateTimeKind.Utc), 5.2m));
        updated.Data.LastCycle.Should().Be(new CompletedCycleSnapshot(41,
            new DateTime(2026, 8, 9, 12, 34, 35, DateTimeKind.Utc), new DateTime(2026, 8, 9, 12, 34, 50, DateTimeKind.Utc),
            new DateTime(2026, 8, 9, 12, 34, 35, 800, DateTimeKind.Utc), 5.1m));
    }

    [Fact]
    public async Task DecodeAsync_NullCycleBlocks_ParsesAsNull()
    {
        using var db = CreateDb();
        var cache = Substitute.For<ITemplateCache>();
        var immId = await SeedImmAsync(db, cache);
        var payload = """
            {
              "timestamp": "2026-08-09T12:34:56.789Z",
              "mode": "idle",
              "sensors": { "cycleCounter": { "value": "0", "error": false } },
              "currentCycle": null,
              "lastCycle": null
            }
            """;
        var sut = CreateSut(db, cache);

        var (success, updated) = await sut.DecodeAsync(new MqttProcessingContext(
            Guid.NewGuid(), $"control/imm/{immId}/telemetry", payload, null, null, null));

        success.Should().BeTrue();
        updated.Data!.CurrentCycle.Should().BeNull();
        updated.Data.LastCycle.Should().BeNull();
    }

    [Theory]
    [InlineData("""{"timestamp":"2026-08-09T12:00:00Z","mode":"auto","sensors":{"cycleCounter":{"value":"1","error":false}}}""")] // no currentCycle/lastCycle keys
    [InlineData("""{"timestamp":"2026-08-09T12:00:00Z","mode":"auto","currentCycle":null,"lastCycle":null}""")] // no sensors
    [InlineData("""{"timestamp":"2026-08-09T12:00:00Z","mode":"auto","sensors":{"matTemp1":{"value":"1","error":false}},"currentCycle":null,"lastCycle":null}""")] // sensors missing cycleCounter
    [InlineData("""{"timestamp":"2026-08-09T12:00:00Z","mode":"offline","sensors":{"cycleCounter":{"value":"1","error":false}},"currentCycle":null,"lastCycle":null}""")] // offline never appears on wire — unknown to contract
    [InlineData("""{"timestamp":"2026-08-09T12:00:00Z","mode":"standby","sensors":{"cycleCounter":{"value":"1","error":false}},"currentCycle":null,"lastCycle":null}""")] // unknown mode
    public async Task DecodeAsync_MissingRequiredSectionOrInvalidMode_Rejected(string payload)
    {
        using var db = CreateDb();
        var cache = Substitute.For<ITemplateCache>();
        var immId = await SeedImmAsync(db, cache);
        var sut = CreateSut(db, cache);

        var (success, _) = await sut.DecodeAsync(new MqttProcessingContext(
            Guid.NewGuid(), $"control/imm/{immId}/telemetry", payload, null, null, null));

        success.Should().BeFalse();
    }

    [Fact]
    public async Task DecodeAsync_SensorWithErrorTrue_PreservesErrorFlag()
    {
        using var db = CreateDb();
        var cache = Substitute.For<ITemplateCache>();
        var immId = await SeedImmAsync(db, cache);
        var payload = """
            {
              "timestamp": "2026-08-09T12:00:00Z",
              "mode": "auto",
              "sensors": {
                "cycleCounter": { "value": "1", "error": false },
                "doorSensor": { "value": "0", "error": true }
              },
              "currentCycle": null,
              "lastCycle": null
            }
            """;
        var sut = CreateSut(db, cache);

        var (success, updated) = await sut.DecodeAsync(new MqttProcessingContext(
            Guid.NewGuid(), $"control/imm/{immId}/telemetry", payload, null, null, null));

        success.Should().BeTrue();
        updated.Data!.Sensors["doorSensor"].Error.Should().BeTrue();
    }
}
```

- [ ] **Step 4: Запустить тесты, убедиться что падают**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~DecodeTelemetryDataHandlerTests"`
Expected: FAIL (компиляция или логика) — `Sensors` всё ещё `Dictionary<string,string>`, `CurrentCycle`/`LastCycle` не парсятся.

- [ ] **Step 5: Реализовать**

Добавить `using Wintime.Control.Core.Constants;` (для `ImmMode`) к списку `using` в начале файла.

Заменить в `DecodeTelemetryDataHandler.cs` блок с шага "2. Verify JSON has required fields" (строки 119-162 в текущем файле) и добавить два новых приватных метода парсинга снапшотов:

```csharp
        // 2. Verify JSON has required top-level sections: mode, sensors, currentCycle, lastCycle
        var rootObj = payloadObject!.AsObject();

        var timestampToken = rootObj["timestamp"];
        if (timestampToken == null)
        {
            _logger.LogError("Payload does not contain 'timestamp' field in topic: {Topic}", context.Topic);
            return (false, context);
        }

        var modeToken = rootObj["mode"];
        if (modeToken == null)
        {
            _logger.LogError("Payload does not contain 'mode' field in topic: {Topic}", context.Topic);
            return (false, context);
        }
        var mode = modeToken.GetValueKind() == JsonValueKind.String
            ? modeToken.GetValue<string>() : modeToken.ToJsonString();
        var normalizedMode = ImmMode.Normalize(mode);
        if (normalizedMode is not (ImmMode.Auto or ImmMode.Manual or ImmMode.Idle or ImmMode.Alarm))
        {
            _logger.LogError("Unknown 'mode' value '{Mode}' in topic: {Topic}", mode, context.Topic);
            return (false, context);
        }

        if (!rootObj.ContainsKey("sensors") || rootObj["sensors"] is not JsonObject sensorsAsObject)
        {
            _logger.LogError("Payload does not contain 'sensors' object in topic: {Topic}", context.Topic);
            return (false, context);
        }

        if (!rootObj.ContainsKey("currentCycle") || !rootObj.ContainsKey("lastCycle"))
        {
            _logger.LogError("Payload missing 'currentCycle'/'lastCycle' section in topic: {Topic}", context.Topic);
            return (false, context);
        }

        // Build sensors dictionary: { name: { value, error } }
        var sensorsDict = new Dictionary<string, SignalValue>();
        foreach (var prop in sensorsAsObject)
        {
            if (prop.Value is not JsonObject sigObj)
                continue;
            var valueToken = sigObj["value"];
            if (valueToken == null)
                continue;
            var value = valueToken.GetValueKind() == JsonValueKind.String
                ? valueToken.GetValue<string>() : valueToken.ToJsonString();
            var error = sigObj["error"] is { } errToken && errToken.GetValueKind() == JsonValueKind.True;
            sensorsDict[prop.Key] = new SignalValue(value, error);
        }

        if (sensorsDict.Count == 0 || !sensorsDict.ContainsKey("cycleCounter"))
        {
            _logger.LogError("Sensors missing or missing reserved 'cycleCounter' key in topic: {Topic}", context.Topic);
            return (false, context);
        }

        CycleSnapshot? currentCycle = null;
        if (rootObj["currentCycle"] is JsonObject ccObj)
        {
            if (!TryParseCycleSnapshot(ccObj, out currentCycle))
            {
                _logger.LogError("Cannot parse 'currentCycle' in topic: {Topic}", context.Topic);
                return (false, context);
            }
        }

        CompletedCycleSnapshot? lastCycle = null;
        if (rootObj["lastCycle"] is JsonObject lcObj)
        {
            if (!TryParseCompletedCycleSnapshot(lcObj, out lastCycle))
            {
                _logger.LogError("Cannot parse 'lastCycle' in topic: {Topic}", context.Topic);
                return (false, context);
            }
        }
```

Далее (после device/template lookup, там где сегодня строится `telemetryMessage`) заменить конструктор:

```csharp
        var telemetryMessage = new MqttTelemetryMessage
        {
            TimestampUtc = timestampUtc,
            DeviceId = deviceId.ToString(),
            Mode = mode,
            Sensors = sensorsDict,
            CurrentCycle = currentCycle,
            LastCycle = lastCycle
        };
```

Добавить два приватных метода парсинга снапшотов (после `TryParseTimestamp`):

```csharp
    private static bool TryParseCycleSnapshot(JsonObject obj, out CycleSnapshot? snapshot)
    {
        snapshot = null;

        if (obj["number"] is not { } numberToken || !numberToken.AsValue().TryGetValue<int>(out var number))
            return false;
        if (obj["startTime"] is not { } startToken || !TryParseTimestamp(startToken, out var startTime))
            return false;

        DateTime? injectionStartTime = null;
        if (obj["injectionStartTime"] is { } injToken)
        {
            if (!TryParseTimestamp(injToken, out var inj))
                return false;
            injectionStartTime = inj;
        }

        decimal? cushion = null;
        if (obj["cushion"] is { } cushionToken)
        {
            if (!cushionToken.AsValue().TryGetValue<decimal>(out var c))
                return false;
            cushion = c;
        }

        snapshot = new CycleSnapshot(number, startTime, injectionStartTime, cushion);
        return true;
    }

    private static bool TryParseCompletedCycleSnapshot(JsonObject obj, out CompletedCycleSnapshot? snapshot)
    {
        snapshot = null;

        if (obj["number"] is not { } numberToken || !numberToken.AsValue().TryGetValue<int>(out var number))
            return false;
        if (obj["startTime"] is not { } startToken || !TryParseTimestamp(startToken, out var startTime))
            return false;
        if (obj["endTime"] is not { } endToken || !TryParseTimestamp(endToken, out var endTime))
            return false;

        DateTime? injectionStartTime = null;
        if (obj["injectionStartTime"] is { } injToken)
        {
            if (!TryParseTimestamp(injToken, out var inj))
                return false;
            injectionStartTime = inj;
        }

        decimal? cushion = null;
        if (obj["cushion"] is { } cushionToken)
        {
            if (!cushionToken.AsValue().TryGetValue<decimal>(out var c))
                return false;
            cushion = c;
        }

        snapshot = new CompletedCycleSnapshot(number, startTime, endTime, injectionStartTime, cushion);
        return true;
    }
```

Добавить `using Wintime.Control.Core.DTOs.Mqtt;` уже присутствует; добавить `using System.Text.Json.Nodes;` уже присутствует (для `JsonObject`).

Убрать старую проверку "3. Verify sensors array is not empty" и старый цикл `foreach(var prop in sensorsAsObject)` строящий `Dictionary<string,string>` — заменены шагом выше.

- [ ] **Step 6: Запустить тесты**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~DecodeTelemetryDataHandlerTests"`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add Wintime.Control.Infrastructure/Handlers/DecodeTelemetryDataHandler.cs Wintime.Control.Tests.Unit/Handlers/DecodeTelemetryDataHandlerTests.cs Wintime.Control.Tests.Unit/Helpers/PipelineTestFixtures.cs
git commit -m "feat(mqtt): decode contract v2 payload (sensors with error flag, currentCycle/lastCycle)"
```

---

### Task 3: `ValidateTelemetryDataHandler` — сокращённый тип-свитч, отбрасывание `error=true`

**Files:**
- Modify: `Wintime.Control.Infrastructure/Handlers/ValidateTelemetryDataHandler.cs`
- Test: `Wintime.Control.Tests.Unit/Handlers/ValidateTelemetryDataHandlerTests.cs`

**Interfaces:**
- Consumes: `MqttTelemetryMessage.Sensors : Dictionary<string, SignalValue>` (Task 1/2).
- Produces: `ValidateAsync` возвращает контекст с `Data.Sensors` — той же формы `Dictionary<string, SignalValue>`, но без `error=true` и без типово-невалидных записей; все оставшиеся записи имеют `Error == false`.

- [ ] **Step 1: Прочитать существующий тестовый файл целиком**

Открыть `Wintime.Control.Tests.Unit/Handlers/ValidateTelemetryDataHandlerTests.cs` — переписать theory-кейсы под новый `SignalValue`-контракт, сохранив кейсы для `float`/`int`/`boolean`/`string`, убрав `cycleCounter`/`injectionDuration`/`cyclePause` из типового свитча (кроме отдельного теста на `cycleCounter` как всегда-валидное зарезервированное имя) и добавив кейс `error=true`.

- [ ] **Step 2: Написать/заменить падающие тесты**

Ключевые новые/изменённые тесты (добавить в существующий файл, заменив прежние `InlineData` со старыми типами):

```csharp
[Theory]
[InlineData("float",   "3.14")]
[InlineData("float",   "-0.5")]
[InlineData("int",     "42")]
[InlineData("int",     "-7")]
[InlineData("boolean", "true")]
[InlineData("boolean", "false")]
[InlineData("string",  "any text")]
public async Task ValidateAsync_ValidSensorValue_SensorPassesThrough(string type, string value)
{
    var immId = Guid.NewGuid();
    var sensor = PipelineTestFixtures.MakeSensor("s1", type, threshold: 0);
    var template = PipelineTestFixtures.MakeTemplate([sensor]);
    var sensors = new Dictionary<string, SignalValue> { ["s1"] = new(value, Error: false) };
    var message = PipelineTestFixtures.MakeMessage(immId, sensors: sensors);
    var context = BuildContext(immId, message, template);
    SetupCacheEntry(immId, context);

    var (success, result) = await _sut.ValidateAsync(context);

    success.Should().BeTrue();
    result.Data!.Sensors.Should().ContainKey("s1");
}

[Theory]
[InlineData("float",   "abc")]
[InlineData("int",     "3.14")]
[InlineData("boolean", "yes")]
public async Task ValidateAsync_InvalidSensorValue_SensorRemovedFromResult(string type, string value)
{
    var immId = Guid.NewGuid();
    var sensor = PipelineTestFixtures.MakeSensor("s1", type, threshold: 0, required: false);
    var template = PipelineTestFixtures.MakeTemplate([sensor]);
    var sensors = new Dictionary<string, SignalValue> { ["s1"] = new(value, Error: false) };
    var message = PipelineTestFixtures.MakeMessage(immId, sensors: sensors);
    var context = BuildContext(immId, message, template);
    SetupCacheEntry(immId, context);

    var (success, result) = await _sut.ValidateAsync(context);

    success.Should().BeTrue();
    result.Data!.Sensors.Should().NotContainKey("s1");
}

[Fact]
public async Task ValidateAsync_SensorWithErrorTrue_IsDropped()
{
    var immId = Guid.NewGuid();
    var sensor = PipelineTestFixtures.MakeSensor("s1", "float", threshold: 0);
    var template = PipelineTestFixtures.MakeTemplate([sensor]);
    var sensors = new Dictionary<string, SignalValue> { ["s1"] = new("1.23", Error: true) };
    var message = PipelineTestFixtures.MakeMessage(immId, sensors: sensors);
    var context = BuildContext(immId, message, template);
    SetupCacheEntry(immId, context);

    var (success, result) = await _sut.ValidateAsync(context);

    success.Should().BeTrue();
    result.Data!.Sensors.Should().NotContainKey("s1", "error=true — сбой чтения, сигнал не публикуется дальше");
}

[Fact]
public async Task ValidateAsync_CycleCounterSensor_AlwaysValidWithoutTemplateEntry()
{
    var immId = Guid.NewGuid();
    var template = PipelineTestFixtures.MakeTemplate([]); // cycleCounter НЕ объявлен в шаблоне
    var sensors = new Dictionary<string, SignalValue> { ["cycleCounter"] = new("42", Error: false) };
    var message = PipelineTestFixtures.MakeMessage(immId, sensors: sensors);
    var context = BuildContext(immId, message, template);
    SetupCacheEntry(immId, context);

    var (success, result) = await _sut.ValidateAsync(context);

    success.Should().BeTrue();
    result.Data!.Sensors["cycleCounter"].Should().Be(new SignalValue("42", false));
}

[Fact]
public async Task ValidateAsync_CycleCounterWithNonIntValue_IsDropped()
{
    var immId = Guid.NewGuid();
    var template = PipelineTestFixtures.MakeTemplate([]);
    var sensors = new Dictionary<string, SignalValue> { ["cycleCounter"] = new("not-a-number", Error: false) };
    var message = PipelineTestFixtures.MakeMessage(immId, sensors: sensors);
    var context = BuildContext(immId, message, template);
    SetupCacheEntry(immId, context);

    var (success, result) = await _sut.ValidateAsync(context);

    success.Should().BeTrue();
    result.Data!.Sensors.Should().NotContainKey("cycleCounter");
}
```

Не забыть обновить сигнатуры вспомогательных `BuildContext`/`SetupCacheEntry` в файле, если они типизированы под старый `Dictionary<string,string>` (привести к `Dictionary<string, SignalValue>` в местах, где строится `ImmCacheEntry`/COV-кеш — см. Step 5).

- [ ] **Step 3: Запустить тесты, убедиться что падают**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~ValidateTelemetryDataHandlerTests"`
Expected: FAIL (компиляция — `ValidateTypes`/`ApplyCovFilter` всё ещё оперируют `Dictionary<string,string>`).

- [ ] **Step 4: Реализовать**

В `ValidateTelemetryDataHandler.cs`:

```csharp
    public Task<(bool, MqttProcessingContext)> ValidateAsync(MqttProcessingContext context)
    {
        var (typeCheckPassed, validSensors) = ValidateTypes(context);
        if (!typeCheckPassed)
            return Task.FromResult<(bool, MqttProcessingContext)>((false, context));

        var outputSensors = ApplyCovFilter(context, validSensors);

        var newMessage = new MqttTelemetryMessage
        {
            TimestampUtc = context.Data!.TimestampUtc,
            DeviceId = context.Data.DeviceId,
            Mode = context.Data.Mode,
            Sensors = outputSensors,
            CurrentCycle = context.Data.CurrentCycle,
            LastCycle = context.Data.LastCycle
        };

        return Task.FromResult((true, context with { Data = newMessage }));
    }

    private (bool Success, Dictionary<string, SignalValue> Sensors) ValidateTypes(MqttProcessingContext context)
    {
        var sensors = context.Data!.Sensors;
        var sensorsByName = context.Template!.Sensors.ToDictionary(s => s.ParameterName);
        var invalidSensors = new List<string>();
        var validSensors = new Dictionary<string, SignalValue>(sensors.Count);

        foreach (var (name, sv) in sensors)
        {
            if (sv.Error)
                continue; // сбой чтения на ТПА — не публикуется дальше

            if (name == "cycleCounter")
            {
                if (int.TryParse(sv.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    validSensors[name] = sv;
                else
                    invalidSensors.Add(name);
                continue;
            }

            if (!sensorsByName.TryGetValue(name, out var sensorTemplate))
                continue; // датчик не описан в шаблоне — пропускаем молча

            if (!TryValidateSensorValue(sv.Value, sensorTemplate, out var error))
            {
                invalidSensors.Add(name);
                _logger.LogWarning(
                    "IMM {ImmId}: sensor '{Sensor}' failed validation — {Error}",
                    context.Device!.Id, name, error);
                continue;
            }

            validSensors[name] = sv;
        }

        foreach (var sensor in sensorsByName.Values.Where(s => s.Required))
        {
            if (!validSensors.ContainsKey(sensor.ParameterName))
            {
                _logger.LogError(
                    "IMM {ImmId}: mandatory sensor '{Sensor}' is missing or has invalid value, topic: {Topic}",
                    context.Device!.Id, sensor.ParameterName, context.Topic);
                return (false, validSensors);
            }
        }

        if (invalidSensors.Count > 0)
        {
            _logger.LogWarning(
                "IMM {ImmId}: {Count} sensor(s) removed due to type/value mismatch: [{Sensors}]",
                context.Device!.Id, invalidSensors.Count, string.Join(", ", invalidSensors));
        }

        return (true, validSensors);
    }

    private static bool TryValidateSensorValue(string value, SensorTemplate sensor, out string error)
    {
        error = string.Empty;

        var typeValid = sensor.ParameterType switch
        {
            "string"  => true,
            "float"   => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _),
            "int"     => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
            "boolean" => bool.TryParse(value, out _),
            _         => false
        };

        if (!typeValid)
        {
            error = $"cannot parse '{value}' as '{sensor.ParameterType}'";
            return false;
        }

        if (sensor.AllowedValues is { Count: > 0 } && !sensor.AllowedValues.Contains(value))
        {
            error = $"'{value}' not in allowed values [{string.Join(", ", sensor.AllowedValues)}]";
            return false;
        }

        return true;
    }

    private Dictionary<string, SignalValue> ApplyCovFilter(
        MqttProcessingContext context,
        Dictionary<string, SignalValue> sensors)
    {
        var immId = context.Device!.Id;
        var template = context.Template!;
        var messageAt = context.Data!.TimestampUtc;

        var entry = _immCache.GetEntry(immId);
        var plainSensors = sensors.ToDictionary(kv => kv.Key, kv => kv.Value.Value);

        if (entry == null)
        {
            _immCache.AddImm(immId, template.DeviceTimeoutSeconds);
            _immCache.UpdateEntry(immId, messageAt, template.DeviceTimeoutSeconds, plainSensors);
            return sensors;
        }

        if (entry.LastMessageAt > messageAt)
        {
            _logger.LogWarning(
                "IMM {ImmId}: out-of-order message (msg={MessageAt:O}, cache={CacheAt:O}), COV skipped",
                immId, messageAt, entry.LastMessageAt);
            return sensors;
        }

        if (!entry.IsOnline)
        {
            _immCache.UpdateEntry(immId, messageAt, template.DeviceTimeoutSeconds, plainSensors);
            return sensors;
        }

        var sensorsByName = template.Sensors.ToDictionary(s => s.ParameterName);
        var outputSensors = new Dictionary<string, SignalValue>(sensors.Count);
        var newCacheValues = new Dictionary<string, string>(entry.SensorValues);

        foreach (var (name, sv) in sensors)
        {
            if (name != "cycleCounter" && !sensorsByName.TryGetValue(name, out _))
                continue;

            if (name == "cycleCounter" || sensorsByName[name].Threshold == 0)
            {
                outputSensors[name] = sv;
                newCacheValues[name] = sv.Value;
                continue;
            }

            var sensorTemplate = sensorsByName[name];
            if (entry.SensorValues.TryGetValue(name, out var cachedValue) &&
                !HasChangedBeyondThreshold(sv.Value, cachedValue, sensorTemplate))
            {
                outputSensors[name] = new SignalValue(cachedValue, Error: false);
            }
            else
            {
                outputSensors[name] = sv;
                newCacheValues[name] = sv.Value;
            }
        }

        _immCache.UpdateEntry(immId, messageAt, template.DeviceTimeoutSeconds, newCacheValues);
        return outputSensors;
    }

    private static bool HasChangedBeyondThreshold(string current, string cached, SensorTemplate sensor)
    {
        if (sensor.ParameterType is "float")
        {
            if (double.TryParse(current, NumberStyles.Float, CultureInfo.InvariantCulture, out var cur) &&
                double.TryParse(cached, NumberStyles.Float, CultureInfo.InvariantCulture, out var cac))
                return Math.Abs(cur - cac) > (double)sensor.Threshold;
        }
        else if (sensor.ParameterType is "int")
        {
            if (int.TryParse(current, out var cur) && int.TryParse(cached, out var cac))
                return Math.Abs(cur - cac) > (double)sensor.Threshold;
        }

        return current != cached;
    }
```

Примечание: `sensorsByName[name].Threshold == 0` в COV-блоке безопасен, т.к. строка выше (`name != "cycleCounter" && !sensorsByName.TryGetValue(...)`) гарантирует, что для не-`cycleCounter` имён запись в `sensorsByName` существует к этому месту.

- [ ] **Step 5: Обновить хелперы теста, если типизированы жёстко**

Проверить `BuildContext`/`SetupCacheEntry` в `ValidateTelemetryDataHandlerTests.cs` — `SetupCacheEntry`, если строит `ImmCacheEntry` из `Dictionary<string,string>`, не меняется (COV-кеш остаётся строковым — только `Sensors` на `MqttTelemetryMessage` сменил тип).

- [ ] **Step 6: Запустить тесты**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~ValidateTelemetryDataHandlerTests"`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add Wintime.Control.Infrastructure/Handlers/ValidateTelemetryDataHandler.cs Wintime.Control.Tests.Unit/Handlers/ValidateTelemetryDataHandlerTests.cs
git commit -m "feat(mqtt): validate contract v2 sensors (drop error=true, reserved cycleCounter, shrunk ParameterType)"
```

---

### Task 4: `StoreTelemetryDataHandler` — чтение `SignalValue`, `cycleCounter` без Template

**Files:**
- Modify: `Wintime.Control.Infrastructure/Handlers/StoreTelemetryDataHandler.cs`
- Test: `Wintime.Control.Tests.Unit/Handlers/StoreTelemetryDataHandlerTests.cs`

**Interfaces:**
- Consumes: `MqttTelemetryMessage.Sensors : Dictionary<string, SignalValue>` (Task 1-3, всегда `Error == false` на входе в Store).
- Produces: без изменений — `Telemetry` строки в БД.

- [ ] **Step 1: Прочитать существующий тестовый файл, привести существующие тесты к новому типу**

Заменить построение `sensors: new Dictionary<string, string> {...}` в существующих тестах на `sensors: new Dictionary<string, SignalValue> { ["x"] = new("value", Error: false) }` — как минимум там, где тесты напрямую конструируют `MqttTelemetryMessage`/`context`.

- [ ] **Step 2: Добавить падающий тест на `cycleCounter`**

```csharp
[Fact]
public async Task SaveAsync_CycleCounterSensor_StoresValueNumericWithoutTemplateEntry()
{
    var context = BuildContext(
        sensors: new Dictionary<string, SignalValue> { ["cycleCounter"] = new("42", Error: false) },
        templateSensors: []); // cycleCounter НЕ в шаблоне

    await CreateSut().SaveAsync(context);

    var row = await _dbContext.Telemetry.SingleAsync();
    row.ParameterName.Should().Be("cycleCounter");
    row.ValueNumeric.Should().Be(42m);
    row.ValueText.Should().BeNull();
}
```

Если хелпер `BuildContext(sensors:, templateSensors:)` в файле типизирован под `Dictionary<string,string>` — обновить его сигнатуру на `Dictionary<string, SignalValue>` и поправить все существующие вызовы аналогично Step 1.

- [ ] **Step 3: Запустить тесты, убедиться что падают**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~StoreTelemetryDataHandlerTests"`
Expected: FAIL (компиляция).

- [ ] **Step 4: Реализовать**

В `StoreTelemetryDataHandler.cs`, заменить тело `SaveAsync`:

```csharp
    public async Task<bool> SaveAsync(MqttProcessingContext context)
    {
        var data = context.Data!;
        var immId = context.Device!.Id;
        var timestamp = data.TimestampUtc;

        var sensorsByName = context.Template!.Sensors.ToDictionary(s => s.ParameterName);

        var entries = new List<Telemetry>(data.Sensors.Count);

        foreach (var (name, sv) in data.Sensors)
        {
            var entry = new Telemetry
            {
                ImmId = immId,
                Timestamp = timestamp,
                ParameterName = name
            };

            if (name == "cycleCounter")
            {
                entry.ValueNumeric = decimal.TryParse(sv.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numC)
                    ? numC
                    : null;
                if (entry.ValueNumeric is null)
                    entry.ValueText = sv.Value;
            }
            else if (sensorsByName.TryGetValue(name, out var sensorTemplate))
            {
                switch (sensorTemplate.ParameterType)
                {
                    case "float":
                        if (decimal.TryParse(sv.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var numF))
                            entry.ValueNumeric = numF;
                        else
                            entry.ValueText = sv.Value;
                        break;
                    case "int":
                        if (decimal.TryParse(sv.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numI))
                            entry.ValueNumeric = numI;
                        else
                            entry.ValueText = sv.Value;
                        break;
                    default: // string, boolean
                        entry.ValueText = sv.Value;
                        break;
                }
            }
            else
            {
                entry.ValueText = sv.Value; // неизвестный датчик — сохраняем как текст
            }

            entries.Add(entry);
        }

        if (entries.Count == 0)
        {
            _logger.LogWarning("IMM {ImmId}: no sensor readings to save, message {MessageId}", immId, context.MessageId);
            return false;
        }

        _dbContext.Telemetry.AddRange(entries);
        await _dbContext.SaveChangesAsync();

        _logger.LogDebug("IMM {ImmId}: saved {Count} telemetry rows at {Timestamp:O}", immId, entries.Count, timestamp);

        return true;
    }
```

- [ ] **Step 5: Запустить тесты**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~StoreTelemetryDataHandlerTests"`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add Wintime.Control.Infrastructure/Handlers/StoreTelemetryDataHandler.cs Wintime.Control.Tests.Unit/Handlers/StoreTelemetryDataHandlerTests.cs
git commit -m "feat(mqtt): store contract v2 sensors (SignalValue, cycleCounter without template entry)"
```

---

### Task 5: `TemplateCache` — убрать спец-случай устаревших типов

**Files:**
- Modify: `Wintime.Control.Infrastructure/Cache/TemplateCache.cs`
- Test: `Wintime.Control.Tests.Unit/Cache/TemplateCacheTests.cs`

**Interfaces:** без изменений сигнатур — только удаление мёртвой ветки.

- [ ] **Step 1: Проверить существующие тесты на старые типы**

```bash
grep -n "injectionDuration\|cyclePause" Wintime.Control.Tests.Unit/Cache/TemplateCacheTests.cs
```

Удалить theory-кейсы `[InlineData("injectionDuration")]`/`[InlineData("cyclePause")]` из `TemplateCacheTests.cs`, если найдены (они проверяли принудительное обнуление threshold — эта механика для контракта v2 больше не нужна, т.к. эти типы не существуют).

- [ ] **Step 2: Запустить тесты, убедиться что билд/тесты чисты после удаления**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~TemplateCacheTests"`
Expected: PASS (тесты, ссылавшиеся на удалённые типы, больше не существуют; остальные проходят как раньше).

- [ ] **Step 3: Реализовать**

В `TemplateCache.cs`, убрать блок:

```csharp
                    // COV-фильтрация для длительностей цикла обязана быть выключена:
                    // при ненулевом пороге фильтр подставит значение прошлого цикла
                    // (ADR-0005, вариант B) и вместо реальной вариации получится ровная
                    // линия — то есть потеряется ровно то, ради чего эти сенсоры заведены.
                    if (type is "injectionDuration" or "cyclePause" && threshold != 0m)
                        threshold = 0m;
```

Тип по умолчанию `"float"` в `s.TryGetProperty("type", out var t) ? t.GetString() ?? "float" : "float"` не меняется.

- [ ] **Step 4: Запустить тесты**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~TemplateCacheTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add Wintime.Control.Infrastructure/Cache/TemplateCache.cs Wintime.Control.Tests.Unit/Cache/TemplateCacheTests.cs
git commit -m "chore(templates): drop injectionDuration/cyclePause special-case (types removed from contract v2)"
```

---

### Task 6: `ImmCycle` entity + EF-миграция

**Files:**
- Modify: `Wintime.Control.Core/Entities/ImmCycle.cs`
- Modify: `Wintime.Control.Infrastructure/Data/ControlDbContext.cs`
- Create: `Wintime.Control.Infrastructure/Migrations/*_MqttContractV2CycleBoundaries.cs` (генерируется EF CLI)

**Interfaces:**
- Produces: `ImmCycle.EndTime : DateTime?`, `ImmCycle.Cushion : decimal?`, `ImmCycle.InjectionStartTime : DateTime?`, `ImmCycle.CycleNumber : int?`.

Data-shape change — без юнит-теста на этом шаге (потребляется и проверяется тестами Task 7).

- [ ] **Step 1: Обновить `ImmCycle.cs`**

```csharp
namespace Wintime.Control.Core.Entities;

public class ImmCycle : BaseEntity
{
    public Guid ImmId { get; set; }
    public Guid? TaskId { get; set; }
    public Guid? MoldId { get; set; }
    public DateTime StartTime { get; set; }

    /// <summary>
    /// <c>null</c> — цикл открыт (получен <c>currentCycle</c>, <c>lastCycle</c> с тем же
    /// номером ещё не пришёл). Открытые циклы уже учитываются в износе формы
    /// (<see cref="Cavities"/>), но не в агрегатах длительности/выпуска.
    /// </summary>
    public DateTime? EndTime { get; set; }
    public int DurationSeconds { get; set; }
    public bool IsSuccessful { get; set; }

    /// <summary>
    /// Снапшот гнёздности ПФ (<see cref="Mold.Cavities"/>) на момент записи цикла.
    /// Mold.Cavities — изменяемое поле (гнёзда могут заглушаться при ремонте), поэтому
    /// выработку исторических циклов нельзя пересчитывать по текущему значению.
    /// Fallback для старых записей (= 0) — брать из <see cref="Mold.Cavities"/>.
    /// </summary>
    public int Cavities { get; set; }

    /// <summary>
    /// Длительность цикла литья, миллисекунды: смыкание ПФ↑ → полное раскрытие↑.
    /// Вычисляется из <see cref="InjectionStartTime"/> и <see cref="EndTime"/> по данным
    /// коннектора (контракт v2, блоки currentCycle/lastCycle).
    /// <c>null</c> — машина не отдаёт сигналы формы (штатный случай).
    /// </summary>
    public int? InjectionDurationMs { get; set; }

    /// <summary>
    /// Длительность паузы ПЕРЕД этим циклом литья, миллисекунды:
    /// предыдущее раскрытие↑ → смыкание↑.
    /// Полный цикл = <see cref="InjectionDurationMs"/> + <see cref="PauseDurationMs"/>,
    /// отдельно не хранится.
    /// </summary>
    public int? PauseDurationMs { get; set; }

    /// <summary>
    /// Момент начала впрыска (контракт v2). Источник для <see cref="InjectionDurationMs"/>.
    /// </summary>
    public DateTime? InjectionStartTime { get; set; }

    /// <summary>
    /// Подушка (минимальное значение сигнала роли InjectionPosition за время впрыска),
    /// вычисляется коннектором. <c>null</c>, пока цикл не завершён или машина не отдаёт
    /// сигнал положения инжекции.
    /// </summary>
    public decimal? Cushion { get; set; }

    /// <summary>
    /// Номер цикла как его видит коннектор (счётчик его автомата состояний). НЕ уникален
    /// сам по себе — обнуляется коннектором между сериями выпуска без разрыва связи.
    /// Идентичность цикла — пара (<see cref="CycleNumber"/>, <see cref="StartTime"/>).
    /// </summary>
    public int? CycleNumber { get; set; }

    // Navigation
    public Imm Imm { get; set; } = null!;
    public ShiftTask? Task { get; set; }
    public Mold? Mold { get; set; }
}
```

- [ ] **Step 2: Обновить `ControlDbContext.cs`**

Заменить блок конфигурации `ImmCycle` (строки 108-119 в текущем файле):

```csharp
        // Конфигурация ImmCycle
        builder.Entity<ImmCycle>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ImmId, e.StartTime });
            entity.HasOne(e => e.Imm).WithMany().HasForeignKey(e => e.ImmId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Task).WithMany().HasForeignKey(e => e.TaskId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.Mold).WithMany().HasForeignKey(e => e.MoldId).OnDelete(DeleteBehavior.SetNull);
            entity.Property(e => e.StartTime).HasColumnType("timestamp with time zone");
            entity.Property(e => e.EndTime).HasColumnType("timestamp with time zone");
            entity.Property(e => e.InjectionStartTime).HasColumnType("timestamp with time zone");
            entity.ToTable("ImmCycles");
        });

        // Идентичность цикла (Number, StartTime) — опора для ЗА-проверки перед открытием
        // строки и защита от дублей на уровне БД (контракт v2, счётчик коннектора легитимно
        // обнуляется между сериями выпуска без разрыва связи, поэтому один Number не уникален).
        builder.Entity<ImmCycle>()
            .HasIndex(e => new { e.ImmId, e.CycleNumber, e.StartTime })
            .IsUnique()
            .HasFilter("\"CycleNumber\" IS NOT NULL")
            .HasDatabaseName("IX_ImmCycles_Imm_CycleNumber_StartTime");
```

(Индекс `IX_ImmCycles_Imm_Orphan` чуть ниже по файлу не трогать — он про другой запрос.)

- [ ] **Step 3: Сгенерировать миграцию**

```bash
dotnet ef migrations add MqttContractV2CycleBoundaries --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API
```

- [ ] **Step 4: Проверить сгенерированный SQL глазами**

Открыть новый файл `Wintime.Control.Infrastructure/Migrations/*_MqttContractV2CycleBoundaries.cs` — убедиться, что `EndTime` меняет `IsRequired` на `false` (не пересоздаёт колонку/не теряет данные), новые колонки `Cushion`/`InjectionStartTime`/`CycleNumber` добавлены как nullable, индекс создан как `UNIQUE ... WHERE "CycleNumber" IS NOT NULL`.

- [ ] **Step 5: Применить миграцию к локальной БД**

```bash
dotnet ef database update --project Wintime.Control.Infrastructure --startup-project Wintime.Control.API
```

Expected: применяется без ошибок (существующие строки `ImmCycles` уже все имеют `EndTime` — переход в nullable не требует backfill).

- [ ] **Step 6: Собрать решение**

```bash
dotnet build Wintime.Control.sln
```

Expected: FAIL на этом шаге ожидаемо — `CycleProcessingHandler.cs` и downstream-потребители (Task 7-9) ещё не адаптированы под `EndTime : DateTime?`. Зафиксировать список ошибок компиляции для следующих задач, не чинить их здесь.

- [ ] **Step 7: Commit**

```bash
git add Wintime.Control.Core/Entities/ImmCycle.cs Wintime.Control.Infrastructure/Data/ControlDbContext.cs Wintime.Control.Infrastructure/Migrations/
git commit -m "feat(db): ImmCycle nullable EndTime + Cushion/InjectionStartTime/CycleNumber (contract v2)"
```

---

### Task 7: `ICycleTracker`/`CycleState` — составной ключ

**Files:**
- Modify: `Wintime.Control.Core/Interfaces/ICycleTracker.cs`

**Interfaces:**
- Produces: `CycleState(int? OpenCycleNumber, DateTime? OpenCycleStartTime, Guid? OpenCycleId)`.

Data-shape change, `CycleTracker.cs` (реализация) не меняется — она уже дженерик по `CycleState`. Тестируется через Task 8.

- [ ] **Step 1: Реализовать**

```csharp
namespace Wintime.Control.Core.Interfaces;

public record CycleState(int? OpenCycleNumber, DateTime? OpenCycleStartTime, Guid? OpenCycleId);

/// <summary>
/// In-memory хранилище состояния отслеживания циклов по каждому ТПА.
/// </summary>
public interface ICycleTracker
{
    CycleState? Get(Guid immId);
    void Set(Guid immId, CycleState state);
}
```

- [ ] **Step 2: Собрать решение, зафиксировать ожидаемые ошибки**

```bash
dotnet build Wintime.Control.sln
```

Expected: FAIL — `CycleProcessingHandler.cs` и его тесты всё ещё конструируют `CycleState` по старой 3-аргументной сигнатуре с несовместимыми типами (`DateTime?, int?, string?`). Это устраняется Task 8.

- [ ] **Step 3: Commit**

```bash
git add Wintime.Control.Core/Interfaces/ICycleTracker.cs
git commit -m "feat(cycles): CycleState keyed by (CycleNumber, StartTime) composite identity"
```

---

### Task 8: `CycleProcessingHandler` — открытие/закрытие по `currentCycle`/`lastCycle`

**Files:**
- Modify: `Wintime.Control.Infrastructure/Handlers/CycleProcessingHandler.cs`
- Test: `Wintime.Control.Tests.Unit/Handlers/CycleProcessingHandlerTests.cs`
- Test: `Wintime.Control.Tests.Unit/Helpers/PipelineTestFixtures.cs`

**Interfaces:**
- Consumes: `MqttTelemetryMessage.CurrentCycle`/`LastCycle` (Task 1-2), `CycleState` (Task 7), `ImmCycle.EndTime/Cushion/InjectionStartTime/CycleNumber` (Task 6).
- Produces: без изменений публичного контракта — `ProcessAsync(MqttProcessingContext, CancellationToken)`.

- [ ] **Step 1: Переписать тестовые фикстуры файла**

В `CycleProcessingHandlerTests.cs` заменить `MakeCycleContext`/`MakeCycleContextWithDurations` на построители под контракт v2:

```csharp
private static MqttProcessingContext MakeContext(
    Guid immId, string mode,
    CycleSnapshot? currentCycle = null, CompletedCycleSnapshot? lastCycle = null,
    DateTime? timestampUtc = null)
{
    var template = PipelineTestFixtures.MakeTemplate([]);
    var sensors = new Dictionary<string, SignalValue> { ["cycleCounter"] = new("0", Error: false) };
    var message = PipelineTestFixtures.MakeMessage(immId,
        sensors: sensors, mode: mode, timestampUtc: timestampUtc,
        currentCycle: currentCycle, lastCycle: lastCycle);
    var device = PipelineTestFixtures.MakeImmDto(immId);
    return PipelineTestFixtures.MakeContext("control/imm/x/telemetry", "{}", data: message, device: device, template: template);
}
```

Удалить `MakeCycleContext`/`MakeCycleContextWithDurations` и все их использования, переписав тесты ниже под `MakeContext`.

- [ ] **Step 2: Написать падающие тесты**

Заменить содержимое файла целиком (сохранив `ThrowingHandler`/`SpyHandler`/`CreateDb`):

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Infrastructure.Data;
using Wintime.Control.Infrastructure.Handlers;
using Wintime.Control.Tests.Unit.Helpers;
using EntityTask = Wintime.Control.Core.Entities.ShiftTask;
using EntityTaskStatus = Wintime.Control.Core.Enums.TaskStatus;
using Imm = Wintime.Control.Core.Entities.Imm;
using Mold = Wintime.Control.Core.Entities.Mold;
using SystemTask = System.Threading.Tasks.Task;

namespace Wintime.Control.Tests.Unit.Handlers;

public class CycleProcessingHandlerTests
{
    private static ControlDbContext CreateDb()
        => new(new DbContextOptionsBuilder<ControlDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class ThrowingHandler : ICycleHandler
    {
        public bool Called { get; private set; }
        public SystemTask HandleAsync(CompletedCycle cycle, CancellationToken ct = default)
        {
            Called = true;
            throw new InvalidOperationException("boom");
        }
    }

    private sealed class SpyHandler : ICycleHandler
    {
        public bool Called { get; private set; }
        public SystemTask HandleAsync(CompletedCycle cycle, CancellationToken ct = default)
        {
            Called = true;
            return SystemTask.CompletedTask;
        }
    }

    private static MqttProcessingContext MakeContext(
        Guid immId, string mode,
        CycleSnapshot? currentCycle = null, CompletedCycleSnapshot? lastCycle = null)
    {
        var template = PipelineTestFixtures.MakeTemplate([]);
        var sensors = new Dictionary<string, SignalValue> { ["cycleCounter"] = new("0", Error: false) };
        var message = PipelineTestFixtures.MakeMessage(immId,
            sensors: sensors, mode: mode, currentCycle: currentCycle, lastCycle: lastCycle);
        var device = PipelineTestFixtures.MakeImmDto(immId);
        return PipelineTestFixtures.MakeContext("control/imm/x/telemetry", "{}", data: message, device: device, template: template);
    }

    [Fact]
    public async SystemTask CurrentCycle_opens_immcycle_row_with_null_end_time()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        await db.SaveChangesAsync();

        var tracker = new Wintime.Control.Infrastructure.Services.CycleTracker();
        var sut = new CycleProcessingHandler(db, tracker, [], NullLogger<CycleProcessingHandler>.Instance);

        var start = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
        var inj = start.AddMilliseconds(800);
        await sut.ProcessAsync(MakeContext(immId, "auto",
            currentCycle: new CycleSnapshot(1, start, inj, Cushion: 5.0m)));

        var cycle = await db.ImmCycles.SingleAsync();
        cycle.StartTime.Should().Be(start);
        cycle.EndTime.Should().BeNull("цикл ещё не завершён");
        cycle.CycleNumber.Should().Be(1);
        cycle.InjectionStartTime.Should().Be(inj);
    }

    [Fact]
    public async SystemTask LastCycle_closes_matching_open_row_and_runs_handlers()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        await db.SaveChangesAsync();

        var tracker = new Wintime.Control.Infrastructure.Services.CycleTracker();
        var spy = new SpyHandler();
        var sut = new CycleProcessingHandler(db, tracker, [spy], NullLogger<CycleProcessingHandler>.Instance);

        var start = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
        var inj = start.AddMilliseconds(800);
        var end = start.AddSeconds(12);

        await sut.ProcessAsync(MakeContext(immId, "auto",
            currentCycle: new CycleSnapshot(1, start, inj, Cushion: 5.2m)));
        await sut.ProcessAsync(MakeContext(immId, "auto",
            lastCycle: new CompletedCycleSnapshot(1, start, end, inj, Cushion: 5.1m)));

        var cycle = await db.ImmCycles.SingleAsync();
        cycle.EndTime.Should().Be(end);
        cycle.DurationSeconds.Should().Be(12);
        cycle.InjectionDurationMs.Should().Be((int)(end - inj).TotalMilliseconds);
        cycle.Cushion.Should().Be(5.1m);
        cycle.IsSuccessful.Should().BeTrue();
        spy.Called.Should().BeTrue();
    }

    [Fact]
    public async SystemTask Handler_failure_does_not_prevent_other_handlers_from_running()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        await db.SaveChangesAsync();

        var tracker = new Wintime.Control.Infrastructure.Services.CycleTracker();
        var throwing = new ThrowingHandler();
        var spy = new SpyHandler();
        var sut = new CycleProcessingHandler(db, tracker, [throwing, spy], NullLogger<CycleProcessingHandler>.Instance);

        var start = DateTime.UtcNow;
        await sut.ProcessAsync(MakeContext(immId, "auto",
            lastCycle: new CompletedCycleSnapshot(1, start, start.AddSeconds(10), null, null)));

        throwing.Called.Should().BeTrue();
        spy.Called.Should().BeTrue("сбой одного хендлера не прерывает конвейер");
    }

    [Fact]
    public async SystemTask LastCycle_without_open_row_creates_and_closes_in_one_step()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        await db.SaveChangesAsync();

        var tracker = new Wintime.Control.Infrastructure.Services.CycleTracker();
        var sut = new CycleProcessingHandler(db, tracker, [], NullLogger<CycleProcessingHandler>.Instance);

        var start = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
        var end = start.AddSeconds(10);
        // Control не видела currentCycle этого цикла (пропущенное сообщение/рестарт Control)
        await sut.ProcessAsync(MakeContext(immId, "auto",
            lastCycle: new CompletedCycleSnapshot(7, start, end, null, null)));

        var cycle = await db.ImmCycles.SingleAsync();
        cycle.StartTime.Should().Be(start);
        cycle.EndTime.Should().Be(end);
        cycle.DurationSeconds.Should().Be(10);
    }

    [Fact]
    public async SystemTask Repeated_lastCycle_with_same_number_and_start_time_is_not_reprocessed()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        await db.SaveChangesAsync();

        var tracker = new Wintime.Control.Infrastructure.Services.CycleTracker();
        var spy = new SpyHandler();
        var sut = new CycleProcessingHandler(db, tracker, [spy], NullLogger<CycleProcessingHandler>.Instance);

        var start = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
        var end = start.AddSeconds(10);
        var last = new CompletedCycleSnapshot(1, start, end, null, null);

        await sut.ProcessAsync(MakeContext(immId, "auto", lastCycle: last));
        await sut.ProcessAsync(MakeContext(immId, "auto", lastCycle: last)); // повтор — блок публикуется в каждом сообщении

        (await db.ImmCycles.CountAsync()).Should().Be(1, "дедупликация по (Number, StartTime)");
    }

    [Fact]
    public async SystemTask Reused_cycle_number_with_different_start_time_creates_two_distinct_rows()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        await db.SaveChangesAsync();

        var tracker = new Wintime.Control.Infrastructure.Services.CycleTracker();
        var sut = new CycleProcessingHandler(db, tracker, [], NullLogger<CycleProcessingHandler>.Instance);

        var start1 = new DateTime(2026, 8, 9, 8, 0, 0, DateTimeKind.Utc);
        var start2 = new DateTime(2026, 8, 9, 14, 0, 0, DateTimeKind.Utc); // серия сбросилась, тот же Number=1

        await sut.ProcessAsync(MakeContext(immId, "auto",
            lastCycle: new CompletedCycleSnapshot(1, start1, start1.AddSeconds(10), null, null)));
        await sut.ProcessAsync(MakeContext(immId, "auto",
            lastCycle: new CompletedCycleSnapshot(1, start2, start2.AddSeconds(10), null, null)));

        (await db.ImmCycles.CountAsync()).Should().Be(2, "разные StartTime — разные физические циклы, несмотря на одинаковый Number");
    }

    [Fact]
    public async SystemTask Tracker_rebuilt_after_control_restart_finds_existing_open_row_via_db_check()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        await db.SaveChangesAsync();

        var start = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
        var current = new CycleSnapshot(1, start, null, null);

        var trackerBeforeRestart = new Wintime.Control.Infrastructure.Services.CycleTracker();
        var sutBeforeRestart = new CycleProcessingHandler(db, trackerBeforeRestart, [], NullLogger<CycleProcessingHandler>.Instance);
        await sutBeforeRestart.ProcessAsync(MakeContext(immId, "auto", currentCycle: current));

        // "Рестарт Control" — новый трекер без памяти, тот же коннектор повторяет currentCycle
        var trackerAfterRestart = new Wintime.Control.Infrastructure.Services.CycleTracker();
        var sutAfterRestart = new CycleProcessingHandler(db, trackerAfterRestart, [], NullLogger<CycleProcessingHandler>.Instance);
        await sutAfterRestart.ProcessAsync(MakeContext(immId, "auto", currentCycle: current));

        (await db.ImmCycles.CountAsync()).Should().Be(1, "ЗА-проверка в БД предотвращает дубль после рестарта Control");
    }

    [Fact]
    public async SystemTask Setup_task_gates_cycle_write_even_with_pending_currentCycle()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        var mold = new Mold { Name = "M", FormId = Guid.NewGuid().ToString(), Cavities = 4 };
        var imm = new Imm { Id = immId, Name = "IMM", IsActive = true };
        var task = new EntityTask { ImmId = immId, MoldId = mold.Id, Mold = mold, Imm = imm, PlanQuantity = 100, Status = EntityTaskStatus.Setup };
        db.AddRange(mold, imm, task);
        await db.SaveChangesAsync();

        var tracker = new Wintime.Control.Infrastructure.Services.CycleTracker();
        var sut = new CycleProcessingHandler(db, tracker, [], NullLogger<CycleProcessingHandler>.Instance);

        await sut.ProcessAsync(MakeContext(immId, "auto",
            currentCycle: new CycleSnapshot(1, DateTime.UtcNow, null, null)));

        (await db.ImmCycles.CountAsync()).Should().Be(0, "Setup (наладка) гасит запись цикла — ShouldProcessCycle=false");
    }

    [Fact]
    public async SystemTask InProgress_cycle_snapshots_cavities_and_task_at_open_time()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        var mold = new Mold { Name = "M", FormId = Guid.NewGuid().ToString(), Cavities = 4 };
        var imm = new Imm { Id = immId, Name = "IMM", IsActive = true };
        var task = new EntityTask { ImmId = immId, MoldId = mold.Id, Mold = mold, Imm = imm, PlanQuantity = 100, Status = EntityTaskStatus.InProgress };
        db.AddRange(mold, imm, task);
        await db.SaveChangesAsync();

        var tracker = new Wintime.Control.Infrastructure.Services.CycleTracker();
        var sut = new CycleProcessingHandler(db, tracker, [], NullLogger<CycleProcessingHandler>.Instance);

        await sut.ProcessAsync(MakeContext(immId, "auto",
            currentCycle: new CycleSnapshot(1, DateTime.UtcNow, null, null)));

        var cycle = await db.ImmCycles.SingleAsync();
        cycle.Cavities.Should().Be(4);
        cycle.TaskId.Should().Be(task.Id);
        cycle.MoldId.Should().Be(mold.Id);
    }

    [Fact]
    public async SystemTask Alarm_mode_at_close_marks_cycle_unsuccessful()
    {
        var immId = Guid.NewGuid();
        using var db = CreateDb();
        db.Imms.Add(new Imm { Id = immId, Name = "IMM", IsActive = true });
        await db.SaveChangesAsync();

        var tracker = new Wintime.Control.Infrastructure.Services.CycleTracker();
        var sut = new CycleProcessingHandler(db, tracker, [], NullLogger<CycleProcessingHandler>.Instance);

        var start = DateTime.UtcNow;
        await sut.ProcessAsync(MakeContext(immId, "auto",
            currentCycle: new CycleSnapshot(1, start, null, null)));
        await sut.ProcessAsync(MakeContext(immId, "ALARM",
            lastCycle: new CompletedCycleSnapshot(1, start, start.AddSeconds(5), null, null)));

        var cycle = await db.ImmCycles.SingleAsync();
        cycle.IsSuccessful.Should().BeFalse("режим ALARM в сообщении, закрывающем цикл, → цикл неуспешен");
    }
}
```

- [ ] **Step 3: Запустить тесты, убедиться что падают**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~CycleProcessingHandlerTests"`
Expected: FAIL (компиляция — `CycleProcessingHandler.cs` всё ещё читает `cycleCounter`-сенсор и строит `CycleState` по старой сигнатуре).

- [ ] **Step 4: Реализовать**

Полностью заменить `CycleProcessingHandler.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.Entities;
using EntityTaskStatus = Wintime.Control.Core.Enums.TaskStatus;
using Wintime.Control.Core.Interfaces;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Core.Policies;
using Wintime.Control.Infrastructure.Data;
using SystemTask = System.Threading.Tasks.Task;

namespace Wintime.Control.Infrastructure.Handlers;

/// <summary>
/// Оркестратор обработки циклов (контракт v2): открывает ImmCycle по currentCycle,
/// закрывает по lastCycle. Идентичность цикла — пара (CycleNumber, StartTime), не
/// один номер (счётчик коннектора легитимно обнуляется между сериями без разрыва связи).
/// </summary>
public class CycleProcessingHandler : ICycleProcessingHandler
{
    private readonly ControlDbContext _db;
    private readonly ICycleTracker _tracker;
    private readonly IEnumerable<ICycleHandler> _handlers;
    private readonly ILogger<CycleProcessingHandler> _logger;

    public CycleProcessingHandler(
        ControlDbContext db,
        ICycleTracker tracker,
        IEnumerable<ICycleHandler> handlers,
        ILogger<CycleProcessingHandler> logger)
    {
        _db = db;
        _tracker = tracker;
        _handlers = handlers;
        _logger = logger;
    }

    public async SystemTask ProcessAsync(MqttProcessingContext context, CancellationToken ct = default)
    {
        var data = context.Data;
        var device = context.Device;
        if (data is null || device is null)
            return;

        var currentMode = ImmMode.Normalize(data.Mode);
        var immId = device.Id;

        var activeTask = await _db.ShiftTasks
            .Include(t => t.Mold)
            .FirstOrDefaultAsync(
                t => t.ImmId == immId
                  && (t.Status == EntityTaskStatus.Setup || t.Status == EntityTaskStatus.InProgress),
                ct);

        var taskStatus = ActiveTaskStatusMap.From(activeTask?.Status);
        if (!CycleProcessingPolicy.ShouldProcessCycle(currentMode, taskStatus))
        {
            _tracker.Set(immId, new CycleState(null, null, null));
            return;
        }

        var state = _tracker.Get(immId);

        // 1. Открыть новую строку по currentCycle, если пара (Number, StartTime) новая
        if (data.CurrentCycle is { } current &&
            (state?.OpenCycleNumber != current.Number || state?.OpenCycleStartTime != current.StartTime))
        {
            var existing = await _db.ImmCycles.FirstOrDefaultAsync(
                c => c.ImmId == immId && c.CycleNumber == current.Number && c.StartTime == current.StartTime, ct);

            if (existing is null)
            {
                var cavities = activeTask?.Mold.Cavities ?? 0;
                var opened = new ImmCycle
                {
                    ImmId = immId,
                    TaskId = activeTask?.Id,
                    MoldId = activeTask?.MoldId,
                    StartTime = current.StartTime,
                    EndTime = null,
                    CycleNumber = current.Number,
                    InjectionStartTime = current.InjectionStartTime,
                    Cavities = cavities,
                    IsSuccessful = true // временно — уточняется при закрытии
                };
                _db.ImmCycles.Add(opened);
                await _db.SaveChangesAsync(ct);
                existing = opened;
                _logger.LogDebug("IMM {ImmId}: cycle {Number}/{StartTime:O} opened", immId, current.Number, current.StartTime);
            }

            state = new CycleState(current.Number, current.StartTime, existing.Id);
            _tracker.Set(immId, state);
        }

        // 2-4. Закрыть строку по lastCycle (или создать+закрыть, если старт не видели)
        if (data.LastCycle is { } last)
        {
            ImmCycle? row = null;
            if (state is { OpenCycleId: { } openId } &&
                state.OpenCycleNumber == last.Number && state.OpenCycleStartTime == last.StartTime)
            {
                row = await _db.ImmCycles.FindAsync([openId], ct);
            }

            row ??= await _db.ImmCycles.FirstOrDefaultAsync(
                c => c.ImmId == immId && c.CycleNumber == last.Number && c.StartTime == last.StartTime, ct);

            if (row is null || row.EndTime is null)
            {
                var isNewRow = row is null;
                row ??= new ImmCycle
                {
                    ImmId = immId,
                    TaskId = activeTask?.Id,
                    MoldId = activeTask?.MoldId,
                    StartTime = last.StartTime,
                    CycleNumber = last.Number,
                    Cavities = activeTask?.Mold.Cavities ?? 0
                };

                row.EndTime = last.EndTime;
                row.InjectionStartTime ??= last.InjectionStartTime;
                row.Cushion = last.Cushion;
                row.DurationSeconds = (int)Math.Round((last.EndTime - row.StartTime).TotalSeconds);
                row.InjectionDurationMs = row.InjectionStartTime.HasValue
                    ? (int)(last.EndTime - row.InjectionStartTime.Value).TotalMilliseconds
                    : null;
                row.IsSuccessful = currentMode != ImmMode.Alarm;

                if (isNewRow)
                    _db.ImmCycles.Add(row);

                await _db.SaveChangesAsync(ct); // СТАДИЯ 1 — цикл долговечен

                if (state?.OpenCycleId == row.Id)
                    _tracker.Set(immId, new CycleState(null, null, null));

                var completed = new CompletedCycle(row, activeTask, currentMode, true);
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
                            handler.GetType().Name, immId, row.Id);
                    }
                }

                _logger.LogDebug("IMM {ImmId}: cycle {Number}/{StartTime:O} closed — duration {Duration}s, successful={Success}",
                    immId, row.CycleNumber, row.StartTime, row.DurationSeconds, row.IsSuccessful);
            }
            // else: row.EndTime уже заполнен — повторная публикация lastCycle, дедупликация
        }
    }
}
```

- [ ] **Step 5: Запустить тесты**

Run: `dotnet test Wintime.Control.Tests.Unit --filter "FullyQualifiedName~CycleProcessingHandlerTests"`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add Wintime.Control.Infrastructure/Handlers/CycleProcessingHandler.cs Wintime.Control.Tests.Unit/Handlers/CycleProcessingHandlerTests.cs Wintime.Control.Tests.Unit/Helpers/PipelineTestFixtures.cs
git commit -m "feat(cycles): open/close ImmCycle from currentCycle/lastCycle with composite-key identity"
```

---

### Task 9: Даунстрим-потребители — nullable `EndTime`

**Files:**
- Modify: `Wintime.Control.Infrastructure/Handlers/UnplannedRunHandler.cs`
- Modify: `Wintime.Control.Infrastructure/Reports/ReportService.cs`
- Modify: `Wintime.Control.API/Controllers/ImmController.cs`
- Modify: `Wintime.Control.API/Controllers/UnplannedRunController.cs`
- Modify: `Wintime.Control.Core/DTOs/Imm/TelemetryDashboardDto.cs`
- Test: `Wintime.Control.Tests.Unit/Handlers/UnplannedRunHandlerTests.cs`

**Interfaces:**
- Consumes: `ImmCycle.EndTime : DateTime?` (Task 6).
- Constraint: агрегаты остатка ресурса формы (`allTimeCycleStats` в `ReportService`) обязаны продолжать учитывать открытые циклы; агрегаты длительности/среднего цикла — нет.

- [ ] **Step 1: `UnplannedRunHandler.cs` — компиляционный фикс**

`completed.Cycle.EndTime` теперь `DateTime?`; хендлер вызывается только на Stage 2, где строка гарантированно только что закрыта (Task 8) — `.Value` безопасен без дополнительной проверки.

```csharp
        _db.UnplannedRuns.Add(new UnplannedRun { ImmId = immId, StartTime = completed.Cycle.EndTime!.Value });
```

Проверить существующий тест `UnplannedRunHandlerTests.cs` — если он строит `ImmCycle` вручную с `EndTime = ...`, компилируется без изменений (присвоение `DateTime` в `DateTime?` неявно допустимо).

- [ ] **Step 2: `ReportService.cs` — фильтр `EndTime != null` на 4 запросах длительности**

Четыре запроса, где используется `cycles.Average(c => c.DurationSeconds)`/`Sum`/`Count` для агрегатов длительности (строки ~97, ~274, ~389, ~491 в текущем файле) — добавить `&& c.EndTime != null` в `Where`:

```csharp
        var cycles = await _context.ImmCycles
            .Where(c => c.ImmId == imm.Id && c.IsSuccessful && c.EndTime != null
                && c.StartTime >= periodStart && c.StartTime < periodEnd)
            .ToListAsync(ct);
```

(повторить для второго вхождения того же паттерна на ~274 и для обоих `periodCycleStats` на ~389/~491, добавив `&& c.EndTime != null` рядом с существующим `c.IsSuccessful`).

**НЕ трогать** `allTimeCycleStats` (строки ~405, ~508) — они считают `TotalCycles` для «остатка ресурса формы» и обязаны включать открытые циклы (незавершённый цикл уже изнашивает форму — см. спеку, раздел "Проблема").

- [ ] **Step 3: `ImmController.cs` — фильтр на подсчёте сирот-циклов, nullable DTO**

Строка ~144, добавить фильтр:

```csharp
                var orphanCycles = await _context.ImmCycles
                    .Where(c => c.TaskId == null && runImmIds.Contains(c.ImmId) && c.EndTime != null && c.EndTime >= minStart)
                    .Select(c => new { c.ImmId, c.EndTime })
                    .ToListAsync();
```

Строка ~150 (`c.EndTime >= run.StartTime`) — `c.EndTime` в анонимном типе уже `DateTime?` после фильтра выше, сравнение `DateTime? >= DateTime` компилируется как есть, менять не нужно.

`TelemetryDashboardDto.cs` — `TelemetryCycleDto.End` меняет тип на nullable (открытый цикл показывается на графике как ещё идущий, не отбрасывается):

```csharp
public class TelemetryCycleDto
{
    public DateTime Start { get; set; }
    public DateTime? End { get; set; }
    public bool IsSuccessful { get; set; }
}
```

Строка ~451 в `ImmController.cs` (`c.EndTime > fromUtc`) — сравнение `DateTime? > DateTime` компилируется (null трактуется как false, открытые циклы, начавшиеся до `effectiveTo`, не попадут в выборку по этому условию одни, но условие `c.StartTime < effectiveTo` их всё равно включит только если оба условия истинны — открытый цикл с `EndTime=null` НЕ пройдёт `c.EndTime > fromUtc`, значит не попадёт на график). Заменить на явное ИЛИ, чтобы открытый цикл не терялся с графика:

```csharp
        var cycles = await _context.ImmCycles
            .Where(c => c.ImmId == id && c.StartTime < effectiveTo && (c.EndTime == null || c.EndTime > fromUtc))
            .OrderBy(c => c.StartTime)
            .Select(c => new TelemetryCycleDto { Start = c.StartTime, End = c.EndTime, IsSuccessful = c.IsSuccessful })
            .ToListAsync();
```

Пометить в PR-описании (не в коде): фронтенд-компонент, рендерящий `TelemetryCycleDto`, должен обработать `End == null` (ongoing cycle) — вне объёма этого backend-плана.

- [ ] **Step 4: `UnplannedRunController.cs` — фильтры + компиляционный фикс**

`ComputeAggregatesAsync` (строка ~279): добавить фильтр в базовый запрос `q`:

```csharp
        var q = _context.ImmCycles.Where(c => c.ImmId == immId && c.EndTime != null && c.EndTime >= startTime);
```

`GetCandidates` cycleAgg-запрос (строка ~90-94): добавить фильтр и `.Value` на `Last`:

```csharp
        var cycleAgg = await _context.ImmCycles
            .Where(c => c.ImmId == run.ImmId && c.TaskId != null && c.EndTime != null)
            .GroupBy(c => c.TaskId!.Value)
            .Select(g => new { TaskId = g.Key, First = g.Min(c => c.StartTime), Last = g.Max(c => c.EndTime!.Value), Count = g.Count() })
            .ToListAsync();
```

`IsAdjacentAsync` cyc-запрос (строка ~210-214): аналогичный фильтр:

```csharp
        var cyc = await _context.ImmCycles
            .Where(c => c.TaskId == task.Id && c.EndTime != null)
            .GroupBy(c => c.TaskId)
            .Select(g => new { First = g.Min(c => c.StartTime), Last = g.Max(c => c.EndTime!.Value), Count = g.Count() })
            .FirstOrDefaultAsync();
```

`RollbackBindingAsync`/второй `windowQuery` (строки ~176, ~252) — **оставить без изменений**: цикл, ещё не завершённый на момент переназначения задания, — известное ограничение, не блокирующее эту спеку (см. спеку, раздел D, про зависшие открытые циклы). Добавить комментарий:

```csharp
        // ПРИМЕЧАНИЕ: открытый цикл (EndTime=null) на момент бэкфилла не попадёт в это
        // окно — известное ограничение контракта v2, не устраняется в этой задаче.
        var windowQuery = _context.ImmCycles.Where(c => c.ImmId == run.ImmId && c.TaskId == null && c.EndTime >= run.StartTime);
```

(тот же комментарий над строкой ~252).

- [ ] **Step 5: Собрать решение**

```bash
dotnet build Wintime.Control.sln
```

Expected: PASS — все компиляционные ошибки, связанные с `EndTime : DateTime?`, устранены.

- [ ] **Step 6: Запустить весь unit-тестовый набор**

```bash
dotnet test Wintime.Control.Tests.Unit
```

Expected: PASS. Если что-то из существующих `UnplannedRunHandlerTests`/`ReportService`-related тестов падает из-за смены типа — поправить конструирование тестовых `ImmCycle` (обычно достаточно, что `EndTime = ...` присваивание компилируется как есть).

- [ ] **Step 7: Commit**

```bash
git add Wintime.Control.Infrastructure/Handlers/UnplannedRunHandler.cs Wintime.Control.Infrastructure/Reports/ReportService.cs Wintime.Control.API/Controllers/ImmController.cs Wintime.Control.API/Controllers/UnplannedRunController.cs Wintime.Control.Core/DTOs/Imm/TelemetryDashboardDto.cs
git commit -m "fix(cycles): guard open (EndTime=null) cycles out of duration aggregates, keep them in mold-wear aggregates"
```

---

## Final Verification

- [ ] Полная сборка решения: `dotnet build Wintime.Control.sln`
- [ ] Полный unit-тестовый набор: `dotnet test Wintime.Control.Tests.Unit`
- [ ] Интеграционный набор (если есть БД для него в окружении): `dotnet test Wintime.Control.Tests.Integration` — особое внимание `TelemetryDashboardTests.cs`, `ImmListUnplannedCycleCountTests.cs`, `UnplannedRunJournalTests.cs`, `AssignTaskTests.cs`, `EpisodeEndClosedAtTests.cs` (все читают `ImmCycle.EndTime`).
- [ ] Грep на мёртвые ссылки старого контракта: `grep -rn "injectionDuration\|cyclePause\|cycleStart\b\|cycleEnd\b" --include=*.cs Wintime.Control.Core Wintime.Control.Infrastructure Wintime.Control.API` — должно быть пусто вне комментариев/XML-доков, объясняющих историю.
- [ ] Обновить `CLAUDE.md`, раздел «Цикл литья и пауза — от коннектора, полный цикл — производный»: origin-предложение про `injectionDuration`/`cyclePause` заменить на описание вывода из `currentCycle`/`lastCycle` контракта v2 (составной ключ `(CycleNumber, StartTime)`, открытие/закрытие одной строки).
- [ ] Написать ADR (`docs/adr/0011-mqtt-contract-v2-cycle-detection.md`, MADR-формат по `docs/adr/README.md`): решение перенести детекцию цикла на коннектор целиком, отменяет ADR-предшественник (если существовал для injectionDuration/cyclePause — по факту не был принят, отдельного ADR не было, только спека 2026-08-01), фиксирует композитный ключ идентичности и разделение "остаток ресурса" vs "агрегаты длительности" для открытых циклов.
- [ ] Оповестить об известных ограничениях, вынесенных в беклог (не в этой задаче): детект зависших открытых циклов по таймауту устройства; `RollbackBindingAsync` не подхватывает открытые циклы при переназначении задания; фронтенд-компонент графика циклов не обновлён под nullable `End`.

Реализация коннектора USR-Modbus под контракт v2 (отдельный репозиторий `Sources/Connectors/Wintime.Connector.UsrModbus`) — отдельная спека и план, не входит в этот план.
