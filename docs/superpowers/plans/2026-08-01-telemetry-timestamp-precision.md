# Миллисекундная точность метки времени телеметрии — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Убрать обрезание метки времени телеметрии до секунды, заменив `MqttTelemetryMessage.Timestamp` (`long`, Unix-секунды) на `TimestampUtc` (`DateTime`, `Kind=Utc`).

**Architecture:** Продюсеры уже шлют ISO-8601 с долями секунды; точность теряется в единственной точке — `DecodeTelemetryDataHandler`, который приводит разобранное время к Unix-секундам, чтобы уложить в поле `long`. Меняется тип поля DTO, две дублирующиеся ветки разбора схлопываются в один парсер, четыре потребителя перестают конвертировать время обратно. Продюсеры, схема БД и миграции не затрагиваются.

**Tech Stack:** .NET 9, ASP.NET Core, EF Core 9 + Npgsql, System.Text.Json (`JsonNode`), xUnit + FluentAssertions + NSubstitute.

**Спека:** `docs/superpowers/specs/2026-08-01-telemetry-timestamp-precision-design.md`

## Global Constraints

- Поле называется `TimestampUtc`, тип `DateTime`, инвариант `Kind == DateTimeKind.Utc`. Нарушение даёт исключение Npgsql на колонке `timestamptz` (правило «DateTime → PostgreSQL» в `CLAUDE.md`).
- Единственный производитель значения — `DecodeTelemetryDataHandler`. Больше нигде `TimestampUtc` не присваивается, кроме тестовых фикстур.
- **Числовой** payload по-прежнему трактуется как Unix-**секунды**. Эвристику «а вдруг миллисекунды» не вводить.
- Собственного округления точности не добавлять: сколько пришло — столько и сохраняем.
- Не трогать: `Wintime.Control.Emulator`, репозиторий коннектора, схему БД, миграции, фронтенд.
- Базовая линия перед началом: `dotnet test Wintime.Control.Tests.Unit` → 324 passed, 0 failed.
- Рабочая ветка: `feature/cycle-time-precision`.

---

### Task 1: Тип поля, парсер и потребители

Замена типа поля ломает компиляцию во всех точках использования, поэтому DTO, парсер, четыре потребителя, фикстура и ожидания существующих тестов меняются одной задачей. Это осознанно: компилятор — гарантия, что ни одна точка не забыта.

**Files:**

- Modify: `Wintime.Control.Core/DTOs/Mqtt/MqttTelemetryMessage.cs:7-10`
- Modify: `Wintime.Control.Infrastructure/Handlers/DecodeTelemetryDataHandler.cs:1-8` (usings), `:192-275` (блок разбора timestamp)
- Modify: `Wintime.Control.Infrastructure/Handlers/StoreTelemetryDataHandler.cs:24`
- Modify: `Wintime.Control.Infrastructure/Handlers/ValidateTelemetryDataHandler.cs:67`, `:189`
- Modify: `Wintime.Control.Infrastructure/Handlers/CycleProcessingHandler.cs:59`
- Modify: `Wintime.Control.Infrastructure/Handlers/UpdateImmStatusHandler.cs:37`
- Test: `Wintime.Control.Tests.Unit/Helpers/PipelineTestFixtures.cs:21-32`
- Test: `Wintime.Control.Tests.Unit/Handlers/DecodeTelemetryDataHandlerTests.cs:165-202`
- Test: `Wintime.Control.Tests.Unit/Handlers/StoreTelemetryDataHandlerTests.cs:206-225`, `:297-312`
- Test: `Wintime.Control.Tests.Unit/Handlers/ValidateTelemetryDataHandlerTests.cs:296-300`, `:323-333`
- Test: `Wintime.Control.Tests.Unit/Handlers/UpdateImmStatusHandlerTests.cs:70-85`

**Interfaces:**

- Consumes: ничего (первая задача).
- Produces:
  - `MqttTelemetryMessage.TimestampUtc` — `DateTime`, `Kind=Utc`, публичное авто-свойство.
  - `PipelineTestFixtures.MakeMessage(Guid immId, Dictionary<string,string>? sensors = null, string? mode = "auto", DateTime? timestampUtc = null)` — параметр переименован из `timestamp` (`long?`).
  - `StoreTelemetryDataHandlerTests.BuildContext(Dictionary<string,string> sensors, IReadOnlyList<SensorTemplate> templateSensors, Guid? immId = null, DateTime? timestampUtc = null)` — приватный хелпер, параметр переименован из `timestamp` (`long?`).

---

- [ ] **Step 1: Переписать тест на ISO-строку под сохранение долей секунды**

Файл `Wintime.Control.Tests.Unit/Handlers/DecodeTelemetryDataHandlerTests.cs`, заменить целиком метод на строках 184–202:

```csharp
    [Fact]
    public async Task DecodeAsync_IsoTimestampString_PreservesSubSecondPrecision()
    {
        var immId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        await SeedImm(immId, templateId);
        _templateCache.GetById(templateId).Returns(PipelineTestFixtures.MakeTemplate());

        var isoTime = "2023-11-14T22:13:20.1230000Z";
        var expected = new DateTime(2023, 11, 14, 22, 13, 20, 123, DateTimeKind.Utc);
        var topic = $"control/imm/{immId}/telemetry";
        var payload = "{\"timestamp\": \"" + isoTime + "\", \"mode\": \"auto\", \"sensors\": {\"s\": \"1\"}}";
        var context = PipelineTestFixtures.MakeContext(topic, payload);

        var (success, result) = await CreateSut().DecodeAsync(context);

        success.Should().BeTrue();
        result.Data!.TimestampUtc.Should().Be(expected);
        result.Data.TimestampUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task DecodeAsync_TwoIsoTimestampsInSameSecond_ProduceDistinctValues()
    {
        var immId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        await SeedImm(immId, templateId);
        _templateCache.GetById(templateId).Returns(PipelineTestFixtures.MakeTemplate());

        var topic = $"control/imm/{immId}/telemetry";
        var first  = "{\"timestamp\": \"2023-11-14T22:13:20.1000000Z\", \"mode\": \"auto\", \"sensors\": {\"s\": \"1\"}}";
        var second = "{\"timestamp\": \"2023-11-14T22:13:20.2000000Z\", \"mode\": \"auto\", \"sensors\": {\"s\": \"1\"}}";

        var (ok1, r1) = await CreateSut().DecodeAsync(PipelineTestFixtures.MakeContext(topic, first));
        var (ok2, r2) = await CreateSut().DecodeAsync(PipelineTestFixtures.MakeContext(topic, second));

        ok1.Should().BeTrue();
        ok2.Should().BeTrue();
        r2.Data!.TimestampUtc.Should().BeAfter(r1.Data!.TimestampUtc,
            "два сообщения внутри одной секунды обязаны различаться по времени");
    }
```

- [ ] **Step 2: Убедиться, что тесты не собираются**

Run: `dotnet test Wintime.Control.Tests.Unit --nologo`
Expected: FAIL — ошибки компиляции `CS1061: 'MqttTelemetryMessage' does not contain a definition for 'TimestampUtc'`. Это ожидаемое «красное» состояние: тип поля ещё не изменён.

- [ ] **Step 3: Заменить поле в DTO**

Файл `Wintime.Control.Core/DTOs/Mqtt/MqttTelemetryMessage.cs`, заменить строки 7–10:

```csharp
    /// <summary>
    /// Момент формирования сообщения продюсером. Всегда UTC (<see cref="DateTimeKind.Utc"/>):
    /// значение уходит в колонку <c>timestamptz</c>, а <c>Kind=Unspecified</c> там даёт
    /// исключение Npgsql. Точность — как пришла от продюсера, собственного округления нет.
    /// Единственный производитель значения — <c>DecodeTelemetryDataHandler</c>.
    /// </summary>
    public DateTime TimestampUtc { get; set; }
```

- [ ] **Step 4: Добавить using в обработчик разбора**

Файл `Wintime.Control.Infrastructure/Handlers/DecodeTelemetryDataHandler.cs`, после строки 4 (`using System.Text.Json;`) добавить:

```csharp
using System.Globalization;
```

- [ ] **Step 5: Схлопнуть две ветки разбора в один парсер**

Файл `Wintime.Control.Infrastructure/Handlers/DecodeTelemetryDataHandler.cs`. Заменить весь блок со строки 192 (комментарий `// Attempt to extract and convert timestamp`) по строку 275 (закрывающая скобка метода `DecodeAsync`) на:

```csharp
        // Нормализация timestamp. ISO-строка сохраняет доли секунды; число трактуется
        // как Unix-СЕКУНДЫ — обратная совместимость с продюсерами, шлющими число.
        // timestampToken объявлен как JsonNode? — на этой строке он уже проверен на null
        // выше (ранний выход «Payload does not contain 'timestamp' field»), поэтому «!».
        if (!TryParseTimestamp(timestampToken!, out var timestampUtc))
        {
            _logger.LogError("Cannot parse timestamp {TimestampValue} in topic: {Topic}",
                timestampToken.ToJsonString(), context.Topic);
            return (false, context);
        }

        var telemetryMessage = new MqttTelemetryMessage
        {
            TimestampUtc = timestampUtc,
            DeviceId = deviceId.ToString(),
            Mode = mode,
            Sensors = sensorsDict
        };

        return (true, context with
        {
            Data = telemetryMessage,
            Device = immDto,
            Template = cachedTemplate
        });
    }

    /// <summary>Границы диапазона, который принимает <see cref="DateTimeOffset.FromUnixTimeSeconds"/>.</summary>
    private const long MinUnixSeconds = -62135596800L;
    private const long MaxUnixSeconds = 253402300799L;

    /// <summary>
    /// Разбирает поле <c>timestamp</c>: ISO-8601 строка (доли секунды сохраняются)
    /// либо число — Unix-секунды. Результат всегда <see cref="DateTimeKind.Utc"/>.
    /// </summary>
    /// <param name="token">Узел JSON со значением поля <c>timestamp</c>.</param>
    /// <param name="utc">Разобранный момент времени в UTC.</param>
    /// <returns><see langword="true"/> при успешном разборе; иначе <see langword="false"/>.</returns>
    private static bool TryParseTimestamp(JsonNode token, out DateTime utc)
    {
        utc = default;

        switch (token.GetValueKind())
        {
            case JsonValueKind.String:
                if (!DateTime.TryParse(
                        token.GetValue<string>(),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.RoundtripKind,
                        out var parsed))
                    return false;

                // Строка с "Z" или со смещением уже приведена к Utc. Строка без указания
                // зоны даёт Kind=Unspecified — трактуем её как UTC: все продюсеры шлют UTC,
                // а Unspecified недопустим для колонки timestamptz.
                utc = parsed.Kind == DateTimeKind.Utc
                    ? parsed
                    : DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
                return true;

            case JsonValueKind.Number:
                if (!token.AsValue().TryGetValue<long>(out var seconds))
                    return false;
                if (seconds < MinUnixSeconds || seconds > MaxUnixSeconds)
                    return false;
                utc = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
                return true;

            default:
                return false;
        }
    }
}
```

Проверить глазами: в файле не осталось ни `existingData`, ни ручной сборки `new MqttProcessingContext(...)`, ни `ToUnixTimeSeconds()`. Выход из `DecodeAsync` теперь один.

- [ ] **Step 6: Обновить четыре потребителя**

`Wintime.Control.Infrastructure/Handlers/StoreTelemetryDataHandler.cs:24`:

```csharp
        var timestamp = data.TimestampUtc;
```

`Wintime.Control.Infrastructure/Handlers/ValidateTelemetryDataHandler.cs:67`:

```csharp
            TimestampUtc = context.Data!.TimestampUtc,
```

`Wintime.Control.Infrastructure/Handlers/ValidateTelemetryDataHandler.cs:189`:

```csharp
        var messageAt = context.Data!.TimestampUtc;
```

`Wintime.Control.Infrastructure/Handlers/CycleProcessingHandler.cs:59`:

```csharp
        var currentTime = data.TimestampUtc;
```

`Wintime.Control.Infrastructure/Handlers/UpdateImmStatusHandler.cs:37`:

```csharp
        var changedAt = context.Data!.TimestampUtc;
```

- [ ] **Step 7: Обновить фикстуру**

Файл `Wintime.Control.Tests.Unit/Helpers/PipelineTestFixtures.cs`, заменить строки 21–32:

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

- [ ] **Step 8: Обновить ожидания в тесте числового payload**

Файл `Wintime.Control.Tests.Unit/Handlers/DecodeTelemetryDataHandlerTests.cs`, заменить строки 180–181 (тело проверок в `DecodeAsync_UnixTimestampNumber_PreservedInResult`):

```csharp
        success.Should().BeTrue();
        result.Data!.TimestampUtc.Should().Be(DateTimeOffset.FromUnixTimeSeconds(unixTs).UtcDateTime);
        result.Data.TimestampUtc.Kind.Should().Be(DateTimeKind.Utc);
```

- [ ] **Step 9: Обновить тесты `StoreTelemetryDataHandlerTests`**

Заменить строки 201–225 (документирующий комментарий и тело теста):

```csharp
    /// <summary>
    /// Поля <c>ImmId</c>, <c>ParameterName</c> и <c>Timestamp</c> должны точно
    /// соответствовать значениям из контекста, включая доли секунды.
    /// </summary>
    [Fact]
    public async Task SaveAsync_SavesCorrectImmIdParameterNameAndTimestamp()
    {
        var immId = Guid.NewGuid();
        var expectedTimestamp = new DateTime(2023, 11, 14, 22, 13, 20, 123, DateTimeKind.Utc);

        var context = BuildContext(
            immId: immId,
            sensors: new Dictionary<string, string> { ["temp"] = "20.0" },
            templateSensors: [PipelineTestFixtures.MakeSensor("temp", "float")],
            timestampUtc: expectedTimestamp);

        await CreateSut().SaveAsync(context);

        var row = await _dbContext.Telemetry.SingleAsync();
        row.ImmId.Should().Be(immId);
        row.ParameterName.Should().Be("temp");
        row.Timestamp.Should().Be(expectedTimestamp);
    }
```

Заменить строки 297–312 (хелпер `BuildContext`):

```csharp
    private static Wintime.Control.Core.DTOs.Mqtt.MqttProcessingContext BuildContext(
        Dictionary<string, string> sensors,
        IReadOnlyList<SensorTemplate> templateSensors,
        Guid? immId = null,
        DateTime? timestampUtc = null)
    {
        var id = immId ?? Guid.NewGuid();
        var ts = timestampUtc ?? DateTime.UtcNow;

        var message  = PipelineTestFixtures.MakeMessage(id, sensors, timestampUtc: ts);
        var device   = PipelineTestFixtures.MakeImmDto(id);
        var template = PipelineTestFixtures.MakeTemplate(templateSensors);

        return PipelineTestFixtures.MakeContext(
            $"control/imm/{id}/telemetry", "{}", data: message, device: device, template: template);
    }
```

- [ ] **Step 10: Обновить тесты `ValidateTelemetryDataHandlerTests`**

Заменить строки 296–300:

```csharp
        // Новое значение в пределах порога, но устройство было offline
        var messageAt = DateTime.UtcNow;
        var message = PipelineTestFixtures.MakeMessage(immId,
            new Dictionary<string, string> { ["temp"] = "20.3" },
            timestampUtc: messageAt);
```

Заменить строку 325 (в хелпере `SetupCacheEntry`):

```csharp
        var messageAt = context.Data!.TimestampUtc;
```

- [ ] **Step 11: Обновить тест `UpdateImmStatusHandlerTests`**

Заменить строки 70–85:

```csharp
    [Fact]
    public async Task UpdateStatusAsync_PassesCorrectTimestampToService()
    {
        var immId = Guid.NewGuid();
        var expectedUtc = new DateTime(2023, 11, 14, 22, 13, 20, 123, DateTimeKind.Utc);

        var device = PipelineTestFixtures.MakeImmDto(immId);
        var message = PipelineTestFixtures.MakeMessage(immId, mode: "auto", timestampUtc: expectedUtc);
        var context = PipelineTestFixtures.MakeContext("control/imm/x/telemetry", "{}", data: message, device: device);

        await _sut.UpdateStatusAsync(context);

        await _statusService.Received(1)
            .UpdateStatusAsync(immId, "Auto", Arg.Is<DateTime>(dt => dt == expectedUtc));
    }
```

- [ ] **Step 12: Собрать решение целиком**

Run: `dotnet build Wintime.Control.sln --nologo`
Expected: `Build succeeded` без ошибок. Если компилятор указывает на ещё не обновлённую точку использования `Timestamp` — обновить её по образцу шага 6 и повторить.

- [ ] **Step 13: Прогнать все юнит-тесты**

Run: `dotnet test Wintime.Control.Tests.Unit --nologo`
Expected: PASS, 325 passed, 0 failed (324 базовых + один новый `DecodeAsync_TwoIsoTimestampsInSameSecond_ProduceDistinctValues`; тест `IsoTimestampString_ConvertedToUnixSeconds` переименован, а не добавлен).

- [ ] **Step 14: Прогнать интеграционные тесты**

Run: `dotnet test Wintime.Control.Tests.Integration --nologo`
Expected: PASS, 0 failed. Прямых обращений к `MqttTelemetryMessage` там нет, прогон нужен как проверка, что конвейер телеметрии не сломан.

- [ ] **Step 15: Коммит**

```bash
git add Wintime.Control.Core/DTOs/Mqtt/MqttTelemetryMessage.cs \
        Wintime.Control.Infrastructure/Handlers/DecodeTelemetryDataHandler.cs \
        Wintime.Control.Infrastructure/Handlers/StoreTelemetryDataHandler.cs \
        Wintime.Control.Infrastructure/Handlers/ValidateTelemetryDataHandler.cs \
        Wintime.Control.Infrastructure/Handlers/CycleProcessingHandler.cs \
        Wintime.Control.Infrastructure/Handlers/UpdateImmStatusHandler.cs \
        Wintime.Control.Tests.Unit/
git commit -m "fix: метка времени телеметрии хранится с долями секунды

MqttTelemetryMessage.Timestamp (long, Unix-секунды) заменён на
TimestampUtc (DateTime, Kind=Utc). Продюсеры шлют ISO-8601 с долями
секунды, точность терялась в DecodeTelemetryDataHandler при приведении
к Unix-секундам: при 10 сообщениях в секунду все получали одинаковую
метку.

Две дублирующиеся ветки разбора (строка/число) схлопнуты в один
TryParseTimestamp; числовой payload по-прежнему означает секунды.
Четыре потребителя читают значение напрямую.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 2: Регрессионные тесты на порядок сообщений

Проверяют не разбор, а два следствия, ради которых всё делалось: строки `Telemetry` внутри одной секунды различимы по времени, и детектор out-of-order в COV-фильтре видит перестановку в пределах секунды.

**Files:**

- Test: `Wintime.Control.Tests.Unit/Handlers/StoreTelemetryDataHandlerTests.cs` (новый тест в конец, перед хелпером `BuildContext` на строке ~297)
- Test: `Wintime.Control.Tests.Unit/Handlers/ValidateTelemetryDataHandlerTests.cs` (новый тест перед секцией «Вспомогательные методы», строка ~309)

**Interfaces:**

- Consumes: `MqttTelemetryMessage.TimestampUtc`; `PipelineTestFixtures.MakeMessage(..., DateTime? timestampUtc)`; `PipelineTestFixtures.MakeImmCacheEntry(Guid immId, DateTime lastMessageAt, IReadOnlyDictionary<string,string>? sensorValues = null, int timeoutSeconds = 60)`; приватные хелперы `BuildContext` из обоих тестовых классов (в `ValidateTelemetryDataHandlerTests` сигнатура — `BuildContext(Guid immId, MqttTelemetryMessage message, CachedTemplate template)`).
- Produces: ничего для последующих задач.

---

- [ ] **Step 1: Написать тест на различимость строк Telemetry**

Файл `Wintime.Control.Tests.Unit/Handlers/StoreTelemetryDataHandlerTests.cs`, добавить перед секцией «Вспомогательный метод»:

```csharp
    /// <summary>
    /// Два сообщения внутри одной секунды должны дать строки с различным
    /// <c>Timestamp</c>. Регрессия: раньше метка обрезалась до секунды, и до 10
    /// сообщений в секунду получали одинаковое время — порядок строк терялся.
    /// </summary>
    [Fact]
    public async Task SaveAsync_MessagesWithinSameSecond_KeepDistinctTimestamps()
    {
        var immId = Guid.NewGuid();
        var first  = new DateTime(2023, 11, 14, 22, 13, 20, 100, DateTimeKind.Utc);
        var second = new DateTime(2023, 11, 14, 22, 13, 20, 200, DateTimeKind.Utc);
        var templateSensors = new[] { PipelineTestFixtures.MakeSensor("temp", "float") };

        await CreateSut().SaveAsync(BuildContext(
            immId: immId,
            sensors: new Dictionary<string, string> { ["temp"] = "20.0" },
            templateSensors: templateSensors,
            timestampUtc: first));

        await CreateSut().SaveAsync(BuildContext(
            immId: immId,
            sensors: new Dictionary<string, string> { ["temp"] = "20.1" },
            templateSensors: templateSensors,
            timestampUtc: second));

        var rows = await _dbContext.Telemetry.OrderBy(t => t.Timestamp).ToListAsync();
        rows.Should().HaveCount(2);
        rows[0].Timestamp.Should().Be(first);
        rows[1].Timestamp.Should().Be(second);
    }
```

- [ ] **Step 2: Написать тест на детект out-of-order внутри секунды**

Файл `Wintime.Control.Tests.Unit/Handlers/ValidateTelemetryDataHandlerTests.cs`, добавить перед секцией «Вспомогательные методы» (строка ~309):

```csharp
    /// <summary>
    /// Сообщение, отставшее на 100 мс от закешированного, должно распознаваться как
    /// out-of-order и проходить мимо COV-фильтра. Регрессия: при обрезании метки до
    /// секунды обе величины совпадали, перестановка внутри секунды не детектировалась
    /// и значение подменялось закешированным.
    /// </summary>
    [Fact]
    public async Task ValidateAsync_MessageOlderByMilliseconds_SkipsCovFilter()
    {
        var immId = Guid.NewGuid();
        var sensor = PipelineTestFixtures.MakeSensor("temp", "float", threshold: 5);
        var template = PipelineTestFixtures.MakeTemplate([sensor], timeoutSeconds: 60);

        // Кеш хранит время более позднего сообщения — в той же секунде, но на 100 мс позже.
        var cachedAt  = new DateTime(2023, 11, 14, 22, 13, 20, 200, DateTimeKind.Utc);
        var messageAt = new DateTime(2023, 11, 14, 22, 13, 20, 100, DateTimeKind.Utc);

        _immCache.GetEntry(immId).Returns(PipelineTestFixtures.MakeImmCacheEntry(
            immId, cachedAt,
            new Dictionary<string, string> { ["temp"] = "20.0" },
            timeoutSeconds: 60));

        // Изменение в пределах порога: нормальный путь подменил бы значение на "20.0".
        var message = PipelineTestFixtures.MakeMessage(immId,
            new Dictionary<string, string> { ["temp"] = "20.3" },
            timestampUtc: messageAt);
        var context = BuildContext(immId, message, template);

        var (success, result) = await _sut.ValidateAsync(context);

        success.Should().BeTrue();
        result.Data!.Sensors["temp"].Should().Be("20.3",
            "сообщение out-of-order проходит без COV-фильтрации");
    }
```

- [ ] **Step 3: Прогнать оба новых теста**

Run: `dotnet test Wintime.Control.Tests.Unit --nologo --filter "FullyQualifiedName~SaveAsync_MessagesWithinSameSecond_KeepDistinctTimestamps|FullyQualifiedName~ValidateAsync_MessageOlderByMilliseconds_SkipsCovFilter"`
Expected: PASS, 2 passed, 0 failed.

Если `ValidateAsync_MessageOlderByMilliseconds_SkipsCovFilter` падает с `"20.0"` вместо `"20.3"` — значит `ValidateTelemetryDataHandler:189` всё ещё конвертирует время через `FromUnixTimeSeconds`; вернуться к Task 1, шаг 6.

- [ ] **Step 4: Прогнать все юнит-тесты**

Run: `dotnet test Wintime.Control.Tests.Unit --nologo`
Expected: PASS, 327 passed, 0 failed.

- [ ] **Step 5: Коммит**

```bash
git add Wintime.Control.Tests.Unit/Handlers/StoreTelemetryDataHandlerTests.cs \
        Wintime.Control.Tests.Unit/Handlers/ValidateTelemetryDataHandlerTests.cs
git commit -m "test: регрессии на порядок сообщений внутри секунды

Строки Telemetry от двух сообщений в одной секунде различимы по времени;
детектор out-of-order в COV-фильтре видит перестановку на 100 мс.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

### Task 3: Отметка в спеке work-mode

Спека `2026-08-01-work-mode-and-two-level-cycle-design.md` в разделе «Чего перенос не даёт» утверждает, что Control округляет границы цикла до секунды. После Task 1 это неверно. Правка документационная, кода не касается.

**Files:**

- Modify: `docs/superpowers/specs/2026-08-01-work-mode-and-two-level-cycle-design.md:227-231`

**Interfaces:**

- Consumes: ничего.
- Produces: ничего.

---

- [ ] **Step 1: Добавить отметку об отмене**

Файл `docs/superpowers/specs/2026-08-01-work-mode-and-two-level-cycle-design.md`, сразу после абзаца, заканчивающегося на строке 231 («…в правильной фазе, а не в разрешении.»), вставить:

```markdown
> **Отменено (2026-08-01).** Абзац выше описывал ограничение, которого больше нет.
> `MqttTelemetryMessage` хранит время как `DateTime TimestampUtc` с долями секунды —
> см. `2026-08-01-telemetry-timestamp-precision-design.md`. Вопрос о том, кто вообще
> должен измерять границы цикла, решён в пользу коннектора —
> см. `2026-08-01-cycle-boundaries-from-connector-design.md`.
```

- [ ] **Step 2: Проверить, что правка не задела соседние разделы**

Run: `git diff --stat docs/superpowers/specs/2026-08-01-work-mode-and-two-level-cycle-design.md`
Expected: `1 file changed, 6 insertions(+)` — только вставка, удалений нет.

- [ ] **Step 3: Коммит**

```bash
git add docs/superpowers/specs/2026-08-01-work-mode-and-two-level-cycle-design.md
git commit -m "docs: отметить отменённым ограничение о секундной точности границ цикла

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
```

---

## Проверка по завершении

- [ ] `dotnet build Wintime.Control.sln --nologo` → Build succeeded
- [ ] `dotnet test Wintime.Control.Tests.Unit --nologo` → 327 passed, 0 failed
- [ ] `dotnet test Wintime.Control.Tests.Integration --nologo` → 0 failed
- [ ] `grep -rn "\.Timestamp\b" --include=*.cs Wintime.Control.Infrastructure Wintime.Control.Core` не находит обращений к полю DTO (совпадения по `Telemetry.Timestamp` — сущности БД — остаются, это другое поле)
- [ ] `grep -rn "FromUnixTimeSeconds" --include=*.cs Wintime.Control.Infrastructure` пусто

## Чего этот план НЕ делает

- Не меняет коннектор, эмулятор, схему БД, миграции и фронтенд.
- Не меняет тип `ImmCycle.DurationSeconds` (`int`, секунды) — систематическая ошибка ±1 с уходит, разрешение поля остаётся прежним.
- Не переносит измерение границ цикла в коннектор — это отдельная спека `2026-08-01-cycle-boundaries-from-connector-design.md` и отдельный план.
