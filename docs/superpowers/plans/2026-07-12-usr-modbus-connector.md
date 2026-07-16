# USR-Modbus Connector — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Достроить над готовым ядром `UsrConnector.Core` верхний слой интеграции с Wintime Control: публикацию `MachineState` как телеметрии в MQTT-контракте Control для множества ТПА одновременно, в кроссплатформенном контейнере.

**Architecture:** Ядро (`UsrConnector.Core` + тесты) переносится и переименовывается в `Wintime.Connector.UsrModbus.Core(.Tests)` — семантика автомата **не меняется**. Новый Host-проект `Wintime.Connector.UsrModbus` (Generic Host + `BackgroundService`): `IMachineSource` (Control API или файл) даёт список ТПА → по `ConnectorAlias` грузится локальный `config/<alias>.json` → на каждый ТПА свой `ConnectorEngine` (все опрашиваются одновременно, `Task.WhenAll`) → `StateTelemetryMapper` превращает `MachineState` в `{mode, sensors}` → `MqttPublisher` шлёт в `control/imm/{immId}/telemetry`. В `Offline` публикация подавляется.

**Tech Stack:** .NET 9 (net9.0, кроссплатформенно), NModbus 3.0.*, MQTTnet 5.0.1.1416, `Microsoft.Extensions.Hosting` 9.0.0, xUnit 2.9.*, Docker.

**Спека:** `docs/superpowers/specs/2026-07-03-usr-modbus-connector-design.md`.
**Источник ядра:** `docs/tasks/usr424-modbus-connector/code_new/` (в репозитории Control).

## Global Constraints

- **Таргет `net9.0`** (кроссплатформенно, без `-windows`), `LangVersion=latest`, `Nullable=enable`, `ImplicitUsings=enable`. Основной рантайм — Linux-контейнер.
- **Read-only Modbus** — только FC 0x01–0x04. Никогда не добавлять пишущие коды (инвариант ядра).
- **Семантику ядра не менять** (инвариант №5 его `CLAUDE.md`): `MachineMode`/`CycleCounter`/`CycleCompletion`/роли/автомат — только механическое переименование неймспейсов, никаких правок логики.
- **MQTT-топик ровно** `control/imm/{immId}/telemetry`. Payload `{ "timestamp", "mode", "sensors" }` — все три поля обязательны, `sensors` непуст.
- **`mode` в нижнем регистре**: `auto` / `idle` / `alarm`. `MachineMode.Offline` → **не публиковать** (Control выведет offline по таймауту).
- **Имена сенсоров = `ParameterName` шаблона ТПА** в Control. Поле счётчика циклов называется `cycleCounter`.
- **Числа — инвариантная культура** (десятичная точка); `bool` → `true`/`false`.
- **Версии пакетов**: `MQTTnet` `5.0.1.1416`, `NModbus` `3.0.*`, `Microsoft.Extensions.Hosting` `9.0.0`, `xunit` `2.9.*`, `Microsoft.NET.Test.Sdk` `17.*`.
- **Размещение**: коннектор — **отдельный git-репозиторий** в `e:/Projects/Control/Sources/Connectors/Wintime.Connector.UsrModbus/` (вне репо Control, как приватные коннекторы Keba/OpcUa). Все `git`-команды задач 1–8 выполняются **в этом каталоге**, после `git init` в задаче 1.

Обозначение: `$CONN` = `e:/Projects/Control/Sources/Connectors/Wintime.Connector.UsrModbus`.

---

## File Structure

Целевой репозиторий `$CONN`:

```
Wintime.Connector.UsrModbus.slnx
CLAUDE.md                              # инварианты коннектора (перенос из ядра + Host-слой)
STATE_MACHINE.md  ARCHITECTURE.md      # перенесены из code_new (док ядра)
Dockerfile  .dockerignore
Wintime.Connector.UsrModbus.Core/      # ЯДРО (перенос+переименование, семантика заморожена)
  *.cs  Wintime.Connector.UsrModbus.Core.csproj
Wintime.Connector.UsrModbus.Core.Tests/
  *.cs  Wintime.Connector.UsrModbus.Core.Tests.csproj
Wintime.Connector.UsrModbus/           # HOST (новый)
  Program.cs
  appsettings.json  appsettings.Development.json
  Wintime.Connector.UsrModbus.csproj
  Models/    Settings.cs  MachineDescriptor.cs
  Mapping/   StateTelemetryMapper.cs
  Api/       IMachineSource.cs  FileMachineSource.cs  ControlApiClient.cs  MachineParser.cs
  Config/    AliasConfigLoader.cs
  Mqtt/      MqttPublisher.cs
  Workers/   UsrModbusPollingWorker.cs
  samples/config/machine-01.json       # пример устройства (по alias)
  samples/machines.json                # пример списка для Source=file
Wintime.Connector.UsrModbus.Tests/     # тесты HOST-слоя (новый)
  *.cs  Wintime.Connector.UsrModbus.Tests.csproj
```

Ответственность файлов Host:
- `Mapping/StateTelemetryMapper.cs` — чистое преобразование `MachineState` → `TelemetryMessage?` (единственное место правил mode/sensors/offline).
- `Api/*` — источник списка ТПА (Control API или файл), парсинг DTO.
- `Config/AliasConfigLoader.cs` — загрузка `config/<alias>.json` в `ConnectorConfig` ядра.
- `Mqtt/MqttPublisher.cs` — тонкий паблишер в контракт Control.
- `Workers/UsrModbusPollingWorker.cs` — оркестрация: список ТПА → движки на устройство → подписка → маппинг → публикация.

---

## Task 1: Перенос и переименование ядра, зелёный build+test

**Files:**
- Create dir: `$CONN/`
- Copy from: `docs/tasks/usr424-modbus-connector/code_new/UsrConnector.Core/*`, `.../UsrConnector.Core.Tests/*`, `.../STATE_MACHINE.md`, `.../ARCHITECTURE.md`, `.../CLAUDE.md`
- Create: `$CONN/Wintime.Connector.UsrModbus.slnx`

**Interfaces:**
- Produces (публичный API ядра, потребляется задачами 4–7):
  - `Wintime.Connector.UsrModbus.Core.ConnectorConfig`, `ConfigLoader.LoadFromFile(string) : ConnectorConfig`, `ConfigLoader.LoadFromJson(string)`
  - `ConnectorEngine(ConnectorConfig config, Func<IModbusReader> readerFactory, DateTimeOffset? startUtc = null)`; свойство `MachineState? Latest`; событие `event Action<MachineState>? StateUpdated`; метод `Task RunAsync(CancellationToken)`
  - `NModbusReader(string host, int port, int timeoutMs) : IModbusReader`
  - `MachineState { MachineMode Mode; long CycleCounter; CycleCompletion LastCycleCompletion; DateTimeOffset TimestampUtc; IReadOnlyDictionary<string, object?> Fields }`
  - `enum MachineMode { Idle, Auto, Alarm, Offline }`
  - `static class WellKnownFields { Cushion, Cushion2, LastCycleDurationMs, Reject, MoldPosition, InjectionPosition }` (string-константы)
  - `ConnectorConfig.Device.{Host,Port,UnitId,TimeoutMs,PollIntervalMs}`

- [ ] **Step 1: Создать каталог и скопировать ядро**

Run (Git Bash):
```bash
CONN="e:/Projects/Control/Sources/Connectors/Wintime.Connector.UsrModbus"
SRC="e:/Projects/Control/Sources/Control/docs/tasks/usr424-modbus-connector/code_new"
mkdir -p "$CONN"
cp -r "$SRC/UsrConnector.Core"        "$CONN/Wintime.Connector.UsrModbus.Core"
cp -r "$SRC/UsrConnector.Core.Tests"  "$CONN/Wintime.Connector.UsrModbus.Core.Tests"
cp "$SRC/STATE_MACHINE.md" "$SRC/ARCHITECTURE.md" "$SRC/CLAUDE.md" "$CONN/"
```

- [ ] **Step 2: Переименовать файлы проектов**

```bash
CONN="e:/Projects/Control/Sources/Connectors/Wintime.Connector.UsrModbus"
mv "$CONN/Wintime.Connector.UsrModbus.Core/UsrConnector.Core.csproj" \
   "$CONN/Wintime.Connector.UsrModbus.Core/Wintime.Connector.UsrModbus.Core.csproj"
mv "$CONN/Wintime.Connector.UsrModbus.Core.Tests/UsrConnector.Core.Tests.csproj" \
   "$CONN/Wintime.Connector.UsrModbus.Core.Tests/Wintime.Connector.UsrModbus.Core.Tests.csproj"
```

- [ ] **Step 3: Заменить неймспейсы во всех .cs и .csproj**

Замена токена `UsrConnector` → `Wintime.Connector.UsrModbus` (покрывает `namespace UsrConnector.Core`, `UsrConnector.Core.Tests`, `using`, `RootNamespace`, `ProjectReference`):
```bash
CONN="e:/Projects/Control/Sources/Connectors/Wintime.Connector.UsrModbus"
find "$CONN" \( -name '*.cs' -o -name '*.csproj' \) -exec sed -i 's/UsrConnector/Wintime.Connector.UsrModbus/g' {} +
```

- [ ] **Step 4: Починить ProjectReference в тест-проекте**

Путь ссылки в тестах после переименования папок. Открыть `$CONN/Wintime.Connector.UsrModbus.Core.Tests/Wintime.Connector.UsrModbus.Core.Tests.csproj` и убедиться, что ссылка указывает на новый путь:
```xml
<ProjectReference Include="..\Wintime.Connector.UsrModbus.Core\Wintime.Connector.UsrModbus.Core.csproj" />
```
(sed из Step 3 заменил имя внутри пути `UsrConnector.Core` → `Wintime.Connector.UsrModbus.Core`; проверить, что папка в пути тоже верна — при необходимости поправить вручную.)

- [ ] **Step 5: Создать solution-файл**

Create `$CONN/Wintime.Connector.UsrModbus.slnx`:
```xml
<Solution>
  <Project Path="Wintime.Connector.UsrModbus.Core/Wintime.Connector.UsrModbus.Core.csproj" />
  <Project Path="Wintime.Connector.UsrModbus.Core.Tests/Wintime.Connector.UsrModbus.Core.Tests.csproj" />
</Solution>
```

- [ ] **Step 6: Собрать и прогнать тесты**

Run:
```bash
cd "e:/Projects/Control/Sources/Connectors/Wintime.Connector.UsrModbus"
dotnet build Wintime.Connector.UsrModbus.slnx
dotnet test  Wintime.Connector.UsrModbus.slnx
```
Expected: build succeeds; все тесты ядра PASS (~25). Если сборка падает на ошибках компиляции (ядро генерировалось без SDK и ранее не компилировалось) — исправить **только** синтаксис/компиляцию, **не** трогая семантику автомата; повторять до зелёного.

- [ ] **Step 7: Инициализировать репозиторий и закоммитить**

```bash
cd "e:/Projects/Control/Sources/Connectors/Wintime.Connector.UsrModbus"
printf 'bin/\nobj/\n' > .gitignore
git init -q && git add -A
git commit -q -m "chore: promote UsrConnector core as Wintime.Connector.UsrModbus.Core (green)"
```

---

## Task 2: Скелет Host-проекта (сборка зелёная)

**Files:**
- Create: `$CONN/Wintime.Connector.UsrModbus/Wintime.Connector.UsrModbus.csproj`
- Create: `$CONN/Wintime.Connector.UsrModbus/Program.cs`
- Create: `$CONN/Wintime.Connector.UsrModbus/Models/Settings.cs`
- Create: `$CONN/Wintime.Connector.UsrModbus/appsettings.json`
- Create: `$CONN/Wintime.Connector.UsrModbus/appsettings.Development.json`
- Modify: `$CONN/Wintime.Connector.UsrModbus.slnx`

**Interfaces:**
- Produces: `Wintime.Connector.UsrModbus.Models.MqttSettings`, `Wintime.Connector.UsrModbus.Models.ConnectorSettings` (см. код ниже).

- [ ] **Step 1: Создать csproj Host**

Create `$CONN/Wintime.Connector.UsrModbus/Wintime.Connector.UsrModbus.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk.Worker">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Wintime.Connector.UsrModbus</RootNamespace>
    <AssemblyName>Wintime.Connector.UsrModbus</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Wintime.Connector.UsrModbus.Tests" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="9.0.0" />
    <PackageReference Include="MQTTnet" Version="5.0.1.1416" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Wintime.Connector.UsrModbus.Core\Wintime.Connector.UsrModbus.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <None Update="appsettings*.json"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Модели настроек**

Create `$CONN/Wintime.Connector.UsrModbus/Models/Settings.cs`:
```csharp
namespace Wintime.Connector.UsrModbus.Models;

/// <summary>Настройки подключения к MQTT-брокеру Control.</summary>
public sealed class MqttSettings
{
    public string BrokerHost { get; set; } = "localhost";
    public int Port { get; set; } = 1883;
    public string TopicTemplate { get; set; } = "control/imm/{immId}/telemetry";
}

/// <summary>Настройки коннектора: источник списка ТПА и каталог конфигураций устройств.</summary>
public sealed class ConnectorSettings
{
    /// <summary>Источник списка ТПА: "api" или "file".</summary>
    public string Source { get; set; } = "api";

    /// <summary>Source="api" → базовый URL Control; Source="file" → путь к machines.json.</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>API-ключ (X-Api-Key). Только при Source="api".</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Каталог с файлами устройств config/&lt;alias&gt;.json.</summary>
    public string ConfigDir { get; set; } = "config";

    /// <summary>Пауза перед повторной загрузкой списка ТПА при ошибке, мс.</summary>
    public int ReconnectDelayMs { get; set; } = 10000;
}
```

- [ ] **Step 3: Минимальный Program.cs**

Create `$CONN/Wintime.Connector.UsrModbus/Program.cs`:
```csharp
using Wintime.Connector.UsrModbus.Models;

var builder = Host.CreateApplicationBuilder(args);

var cfg = builder.Configuration;
var mqttSettings      = cfg.GetSection("Mqtt").Get<MqttSettings>()           ?? new MqttSettings();
var connectorSettings = cfg.GetSection("Connector").Get<ConnectorSettings>() ?? new ConnectorSettings();

builder.Services.AddSingleton(mqttSettings);
builder.Services.AddSingleton(connectorSettings);

var host = builder.Build();
host.Run();
```

- [ ] **Step 4: appsettings**

Create `$CONN/Wintime.Connector.UsrModbus/appsettings.json`:
```json
{
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.Hosting.Lifetime": "Information" } },
  "Mqtt": {
    "BrokerHost": "mosquitto.company.local",
    "Port": 1883,
    "TopicTemplate": "control/imm/{immId}/telemetry"
  },
  "Connector": {
    "Source": "api",
    "SourcePath": "https://control.company.local:7001",
    "ApiKey": "your-secret-key-here",
    "ConfigDir": "config",
    "ReconnectDelayMs": 10000
  }
}
```

Create `$CONN/Wintime.Connector.UsrModbus/appsettings.Development.json`:
```json
{
  "Connector": { "Source": "file", "SourcePath": "samples/machines.json", "ConfigDir": "samples/config" }
}
```

- [ ] **Step 5: Добавить Host в solution, собрать**

```bash
cd "e:/Projects/Control/Sources/Connectors/Wintime.Connector.UsrModbus"
dotnet sln Wintime.Connector.UsrModbus.slnx add Wintime.Connector.UsrModbus/Wintime.Connector.UsrModbus.csproj
dotnet build Wintime.Connector.UsrModbus.slnx
```
Expected: build succeeds.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -q -m "feat: host skeleton (settings, DI, appsettings)"
```

---

## Task 3: StateTelemetryMapper (TDD)

**Files:**
- Create: `$CONN/Wintime.Connector.UsrModbus.Tests/Wintime.Connector.UsrModbus.Tests.csproj`
- Create: `$CONN/Wintime.Connector.UsrModbus.Tests/StateTelemetryMapperTests.cs`
- Create: `$CONN/Wintime.Connector.UsrModbus/Mapping/StateTelemetryMapper.cs`
- Modify: `$CONN/Wintime.Connector.UsrModbus.slnx`

**Interfaces:**
- Consumes: `MachineState`, `MachineMode`, `WellKnownFields` (Core, Task 1).
- Produces:
  - `Wintime.Connector.UsrModbus.Mapping.TelemetryMessage(string Mode, IReadOnlyDictionary<string,string> Sensors)`
  - `Wintime.Connector.UsrModbus.Mapping.StateTelemetryMapper.Map(MachineState state) : TelemetryMessage?` (null = не публиковать)
  - `StateTelemetryMapper.CycleCounterField` = `"cycleCounter"`

- [ ] **Step 1: Создать тест-проект**

Create `$CONN/Wintime.Connector.UsrModbus.Tests/Wintime.Connector.UsrModbus.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.9.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Wintime.Connector.UsrModbus\Wintime.Connector.UsrModbus.csproj" />
    <ProjectReference Include="..\Wintime.Connector.UsrModbus.Core\Wintime.Connector.UsrModbus.Core.csproj" />
  </ItemGroup>

</Project>
```

Run:
```bash
cd "e:/Projects/Control/Sources/Connectors/Wintime.Connector.UsrModbus"
dotnet sln Wintime.Connector.UsrModbus.slnx add Wintime.Connector.UsrModbus.Tests/Wintime.Connector.UsrModbus.Tests.csproj
```

- [ ] **Step 2: Написать падающие тесты**

Create `$CONN/Wintime.Connector.UsrModbus.Tests/StateTelemetryMapperTests.cs`:
```csharp
using Wintime.Connector.UsrModbus.Core;
using Wintime.Connector.UsrModbus.Mapping;
using Xunit;

namespace Wintime.Connector.UsrModbus.Tests;

public class StateTelemetryMapperTests
{
    private static MachineState State(MachineMode mode, long counter,
        Dictionary<string, object?>? fields = null) => new()
    {
        Mode = mode,
        CycleCounter = counter,
        LastCycleCompletion = CycleCompletion.Normal,
        TimestampUtc = DateTimeOffset.UnixEpoch,
        Fields = fields ?? new Dictionary<string, object?>(),
    };

    [Theory]
    [InlineData(MachineMode.Auto, "auto")]
    [InlineData(MachineMode.Idle, "idle")]
    [InlineData(MachineMode.Alarm, "alarm")]
    public void Maps_mode_to_lowercase_string(MachineMode mode, string expected)
    {
        var msg = StateTelemetryMapper.Map(State(mode, 5));
        Assert.NotNull(msg);
        Assert.Equal(expected, msg!.Mode);
    }

    [Fact]
    public void Offline_suppresses_publication()
    {
        Assert.Null(StateTelemetryMapper.Map(State(MachineMode.Offline, 5)));
    }

    [Fact]
    public void Always_includes_cycleCounter_as_invariant_integer()
    {
        var msg = StateTelemetryMapper.Map(State(MachineMode.Auto, 42));
        Assert.Equal("42", msg!.Sensors["cycleCounter"]);
    }

    [Fact]
    public void Serializes_double_field_with_dot_and_bool_lowercase()
    {
        var fields = new Dictionary<string, object?>
        {
            [WellKnownFields.Cushion] = 12.345,
            [WellKnownFields.Reject] = true,
        };
        var msg = StateTelemetryMapper.Map(State(MachineMode.Auto, 1, fields));
        Assert.Equal("12.345", msg!.Sensors["cushion"]);
        Assert.Equal("true", msg.Sensors["reject"]);
    }

    [Fact]
    public void Skips_null_field_values()
    {
        var fields = new Dictionary<string, object?> { ["spare"] = null };
        var msg = StateTelemetryMapper.Map(State(MachineMode.Idle, 1, fields));
        Assert.False(msg!.Sensors.ContainsKey("spare"));
    }
}
```

- [ ] **Step 3: Прогнать — убедиться, что не компилируется/падает**

Run:
```bash
dotnet test Wintime.Connector.UsrModbus.slnx --filter FullyQualifiedName~StateTelemetryMapperTests
```
Expected: FAIL — тип `StateTelemetryMapper` не найден (не скомпилируется).

- [ ] **Step 4: Реализовать маппер**

Create `$CONN/Wintime.Connector.UsrModbus/Mapping/StateTelemetryMapper.cs`:
```csharp
using System.Globalization;
using Wintime.Connector.UsrModbus.Core;

namespace Wintime.Connector.UsrModbus.Mapping;

/// <summary>Готовое к публикации сообщение телеметрии Control.</summary>
public sealed record TelemetryMessage(string Mode, IReadOnlyDictionary<string, string> Sensors);

/// <summary>
/// Чистое преобразование MachineState → TelemetryMessage в контракте Control.
/// Единственное место правил mode/sensors/offline. Возврат null = не публиковать (Offline).
/// </summary>
public static class StateTelemetryMapper
{
    public const string CycleCounterField = "cycleCounter";

    public static TelemetryMessage? Map(MachineState state)
    {
        var mode = state.Mode switch
        {
            MachineMode.Auto  => "auto",
            MachineMode.Idle  => "idle",
            MachineMode.Alarm => "alarm",
            _                 => null, // Offline → подавляем публикацию
        };
        if (mode is null) return null;

        var sensors = new Dictionary<string, string>
        {
            [CycleCounterField] = state.CycleCounter.ToString(CultureInfo.InvariantCulture),
        };

        foreach (var (key, value) in state.Fields)
        {
            var formatted = Format(value);
            if (formatted is not null) sensors[key] = formatted;
        }

        return new TelemetryMessage(mode, sensors);
    }

    private static string? Format(object? value) => value switch
    {
        null      => null,
        bool b    => b ? "true" : "false",
        double d  => d.ToString("0.###", CultureInfo.InvariantCulture),
        float f   => ((double)f).ToString("0.###", CultureInfo.InvariantCulture),
        _         => Convert.ToString(value, CultureInfo.InvariantCulture),
    };
}
```

- [ ] **Step 5: Прогнать — зелёные**

Run:
```bash
dotnet test Wintime.Connector.UsrModbus.slnx --filter FullyQualifiedName~StateTelemetryMapperTests
```
Expected: PASS (все кейсы).

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -q -m "feat: StateTelemetryMapper (MachineState -> Control telemetry)"
```

---

## Task 4: Источник списка ТПА (IMachineSource: file + api) (TDD)

**Files:**
- Create: `$CONN/Wintime.Connector.UsrModbus/Models/MachineDescriptor.cs`
- Create: `$CONN/Wintime.Connector.UsrModbus/Api/IMachineSource.cs`
- Create: `$CONN/Wintime.Connector.UsrModbus/Api/MachineParser.cs`
- Create: `$CONN/Wintime.Connector.UsrModbus/Api/FileMachineSource.cs`
- Create: `$CONN/Wintime.Connector.UsrModbus/Api/ControlApiClient.cs`
- Create: `$CONN/Wintime.Connector.UsrModbus.Tests/MachineParserTests.cs`

**Interfaces:**
- Produces:
  - `Wintime.Connector.UsrModbus.Models.MachineDescriptor { Guid ImmId; string ImmName; string? ConnectorAlias }`
  - `Wintime.Connector.UsrModbus.Api.IMachineSource.GetMachinesAsync(CancellationToken) : Task<List<MachineDescriptor>>`
  - `Wintime.Connector.UsrModbus.Api.MachineParser.Parse(string json) : List<MachineDescriptor>`
  - `FileMachineSource(ConnectorSettings) : IMachineSource`, `ControlApiClient(ConnectorSettings, ILogger<ControlApiClient>) : IMachineSource`
  - `ControlApiClient.ConnectorType` = `"usr-modbus"`

- [ ] **Step 1: Модель и интерфейс**

Create `$CONN/Wintime.Connector.UsrModbus/Models/MachineDescriptor.cs`:
```csharp
namespace Wintime.Connector.UsrModbus.Models;

/// <summary>ТПА, полученный от Control: идентификатор + alias для связки с локальным конфигом.</summary>
public sealed class MachineDescriptor
{
    public Guid ImmId { get; set; }
    public string ImmName { get; set; } = string.Empty;
    public string? ConnectorAlias { get; set; }
}
```

Create `$CONN/Wintime.Connector.UsrModbus/Api/IMachineSource.cs`:
```csharp
using Wintime.Connector.UsrModbus.Models;

namespace Wintime.Connector.UsrModbus.Api;

public interface IMachineSource
{
    Task<List<MachineDescriptor>> GetMachinesAsync(CancellationToken ct);
}
```

- [ ] **Step 2: Написать падающие тесты парсера**

Create `$CONN/Wintime.Connector.UsrModbus.Tests/MachineParserTests.cs`:
```csharp
using Wintime.Connector.UsrModbus.Api;
using Xunit;

namespace Wintime.Connector.UsrModbus.Tests;

public class MachineParserTests
{
    [Fact]
    public void Parses_immId_name_alias_case_insensitively()
    {
        const string json = """
        [ { "immId": "11111111-1111-1111-1111-111111111111",
            "immName": "ТПА-1", "connectorAlias": "machine-01",
            "templateConfig": { "ignored": true } } ]
        """;
        var list = MachineParser.Parse(json);
        Assert.Single(list);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), list[0].ImmId);
        Assert.Equal("ТПА-1", list[0].ImmName);
        Assert.Equal("machine-01", list[0].ConnectorAlias);
    }

    [Fact]
    public void Empty_array_yields_empty_list()
    {
        Assert.Empty(MachineParser.Parse("[]"));
    }
}
```

- [ ] **Step 3: Прогнать — падает**

Run:
```bash
dotnet test Wintime.Connector.UsrModbus.slnx --filter FullyQualifiedName~MachineParserTests
```
Expected: FAIL — `MachineParser` не найден.

- [ ] **Step 4: Реализовать парсер**

Create `$CONN/Wintime.Connector.UsrModbus/Api/MachineParser.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Wintime.Connector.UsrModbus.Models;

namespace Wintime.Connector.UsrModbus.Api;

public static class MachineParser
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static List<MachineDescriptor> Parse(string json)
    {
        var dtos = JsonSerializer.Deserialize<List<ApiMachineDto>>(json, JsonOptions)
                   ?? new List<ApiMachineDto>();
        return dtos.Select(d => new MachineDescriptor
        {
            ImmId = d.ImmId,
            ImmName = d.ImmName,
            ConnectorAlias = d.ConnectorAlias,
        }).ToList();
    }

    private sealed class ApiMachineDto
    {
        public Guid ImmId { get; set; }
        public string ImmName { get; set; } = string.Empty;
        public string? ConnectorAlias { get; set; }
        [JsonPropertyName("templateConfig")] public JsonElement? TemplateConfig { get; set; }
    }
}
```

- [ ] **Step 5: Прогнать — зелёные**

Run:
```bash
dotnet test Wintime.Connector.UsrModbus.slnx --filter FullyQualifiedName~MachineParserTests
```
Expected: PASS.

- [ ] **Step 6: Реализовать FileMachineSource и ControlApiClient**

Create `$CONN/Wintime.Connector.UsrModbus/Api/FileMachineSource.cs`:
```csharp
using Wintime.Connector.UsrModbus.Models;

namespace Wintime.Connector.UsrModbus.Api;

/// <summary>Список ТПА из локального JSON-файла (Source="file"). Формат — как ответ API.</summary>
public sealed class FileMachineSource : IMachineSource
{
    private readonly ConnectorSettings _settings;
    public FileMachineSource(ConnectorSettings settings) => _settings = settings;

    public async Task<List<MachineDescriptor>> GetMachinesAsync(CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(_settings.SourcePath, ct);
        return MachineParser.Parse(json);
    }
}
```

Create `$CONN/Wintime.Connector.UsrModbus/Api/ControlApiClient.cs`:
```csharp
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Wintime.Connector.UsrModbus.Models;

namespace Wintime.Connector.UsrModbus.Api;

/// <summary>Список ТПА из Control (Source="api"): GET /api/connectors/usr-modbus/machines.</summary>
public sealed class ControlApiClient : IMachineSource
{
    public const string ConnectorType = "usr-modbus";

    private readonly HttpClient _http;
    private readonly ILogger<ControlApiClient> _logger;

    public ControlApiClient(ConnectorSettings settings, ILogger<ControlApiClient> logger)
    {
        _logger = logger;
        _http = new HttpClient { BaseAddress = new Uri(settings.SourcePath) };
        _http.DefaultRequestHeaders.Add("X-Api-Key", settings.ApiKey);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<List<MachineDescriptor>> GetMachinesAsync(CancellationToken ct)
    {
        var url = $"/api/connectors/{ConnectorType}/machines";
        _logger.LogInformation("Fetching machines from {Url}", url);
        var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct);
        var result = MachineParser.Parse(json);
        _logger.LogInformation("Loaded {Count} machines", result.Count);
        return result;
    }
}
```

- [ ] **Step 7: Собрать, commit**

Run:
```bash
dotnet build Wintime.Connector.UsrModbus.slnx
```
Expected: build succeeds.
```bash
git add -A && git commit -q -m "feat: IMachineSource (file + Control API) with parser tests"
```

---

## Task 5: AliasConfigLoader (TDD)

**Files:**
- Create: `$CONN/Wintime.Connector.UsrModbus/Config/AliasConfigLoader.cs`
- Create: `$CONN/Wintime.Connector.UsrModbus.Tests/AliasConfigLoaderTests.cs`

**Interfaces:**
- Consumes: `ConnectorConfig`, `ConfigLoader.LoadFromFile` (Core, Task 1).
- Produces: `Wintime.Connector.UsrModbus.Config.AliasConfigLoader.TryLoad(string configDir, string alias, out ConnectorConfig? config) : bool`

- [ ] **Step 1: Написать падающие тесты**

Create `$CONN/Wintime.Connector.UsrModbus.Tests/AliasConfigLoaderTests.cs`:
```csharp
using Wintime.Connector.UsrModbus.Config;
using Xunit;

namespace Wintime.Connector.UsrModbus.Tests;

public class AliasConfigLoaderTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "usrcfg-" + Guid.NewGuid().ToString("N"));

    public AliasConfigLoaderTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private const string MinimalConfig = """
    { "device": { "host": "192.168.0.7" },
      "profile": "singleNode",
      "registers": [
        { "name": "Injection", "address": "0x0020", "access": "discreteInput", "role": "injection" },
        { "name": "EjectorFwdReached", "address": "0x0021", "access": "discreteInput", "role": "ejectorFwdReached" }
      ] }
    """;

    [Fact]
    public void Loads_config_for_existing_alias_file()
    {
        File.WriteAllText(Path.Combine(_dir, "machine-01.json"), MinimalConfig);
        var ok = AliasConfigLoader.TryLoad(_dir, "machine-01", out var config);
        Assert.True(ok);
        Assert.NotNull(config);
        Assert.Equal("192.168.0.7", config!.Device.Host);
    }

    [Fact]
    public void Returns_false_for_missing_alias_file()
    {
        var ok = AliasConfigLoader.TryLoad(_dir, "no-such", out var config);
        Assert.False(ok);
        Assert.Null(config);
    }
}
```
(Примечание: строку временного каталога в поле `_dir` можно упростить до `Path.Combine(Path.GetTempPath(), "usrcfg-" + Guid.NewGuid().ToString("N"))` — приведите к этому виду при вводе.)

- [ ] **Step 2: Прогнать — падает**

Run:
```bash
dotnet test Wintime.Connector.UsrModbus.slnx --filter FullyQualifiedName~AliasConfigLoaderTests
```
Expected: FAIL — `AliasConfigLoader` не найден.

- [ ] **Step 3: Реализовать загрузчик**

Create `$CONN/Wintime.Connector.UsrModbus/Config/AliasConfigLoader.cs`:
```csharp
using Wintime.Connector.UsrModbus.Core;

namespace Wintime.Connector.UsrModbus.Config;

/// <summary>Загрузка конфигурации устройства по alias: &lt;configDir&gt;/&lt;alias&gt;.json.</summary>
public static class AliasConfigLoader
{
    public static bool TryLoad(string configDir, string alias, out ConnectorConfig? config)
    {
        config = null;
        var path = Path.Combine(configDir, alias + ".json");
        if (!File.Exists(path)) return false;
        config = ConfigLoader.LoadFromFile(path);
        return true;
    }
}
```

- [ ] **Step 4: Прогнать — зелёные**

Run:
```bash
dotnet test Wintime.Connector.UsrModbus.slnx --filter FullyQualifiedName~AliasConfigLoaderTests
```
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -q -m "feat: AliasConfigLoader (config/<alias>.json)"
```

---

## Task 6: MqttPublisher

**Files:**
- Create: `$CONN/Wintime.Connector.UsrModbus/Mqtt/MqttPublisher.cs`

**Interfaces:**
- Consumes: `MqttSettings` (Task 2), `TelemetryMessage` (Task 3).
- Produces:
  - `Wintime.Connector.UsrModbus.Mqtt.MqttPublisher(MqttSettings, ILogger<MqttPublisher>)`
  - `ConnectAsync(CancellationToken) : Task`
  - `PublishTelemetryAsync(Guid immId, TelemetryMessage message, DateTimeOffset timestamp, CancellationToken) : Task`
  - реализует `IAsyncDisposable`

- [ ] **Step 1: Реализовать паблишер**

Create `$CONN/Wintime.Connector.UsrModbus/Mqtt/MqttPublisher.cs`:
```csharp
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MQTTnet;
using Wintime.Connector.UsrModbus.Mapping;
using Wintime.Connector.UsrModbus.Models;

namespace Wintime.Connector.UsrModbus.Mqtt;

/// <summary>Тонкий паблишер телеметрии в контракт Control: control/imm/{immId}/telemetry.</summary>
public sealed class MqttPublisher : IAsyncDisposable
{
    private readonly MqttSettings _settings;
    private readonly ILogger<MqttPublisher> _logger;
    private readonly IMqttClient _client;

    public MqttPublisher(MqttSettings settings, ILogger<MqttPublisher> logger)
    {
        _settings = settings;
        _logger = logger;
        _client = new MqttClientFactory().CreateMqttClient();
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(_settings.BrokerHost, _settings.Port)
            .WithClientId($"usr-modbus-connector-{Guid.NewGuid()}")
            .Build();
        await _client.ConnectAsync(options, ct);
        _logger.LogInformation("MQTT connected to {Host}:{Port}", _settings.BrokerHost, _settings.Port);
    }

    public async Task PublishTelemetryAsync(Guid immId, TelemetryMessage message,
        DateTimeOffset timestamp, CancellationToken ct)
    {
        if (!_client.IsConnected) return;

        var payload = new
        {
            timestamp = timestamp.UtcDateTime.ToString("O"),
            mode = message.Mode,
            sensors = message.Sensors,
        };
        var json = JsonSerializer.Serialize(payload);
        var topic = _settings.TopicTemplate.Replace("{immId}", immId.ToString());

        var mqttMessage = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(json)
            .WithQualityOfServiceLevel(MQTTnet.Protocol.MqttQualityOfServiceLevel.AtLeastOnce)
            .Build();

        await _client.PublishAsync(mqttMessage, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client.IsConnected) await _client.DisconnectAsync();
        _client.Dispose();
    }
}
```

- [ ] **Step 2: Собрать, commit**

Run:
```bash
dotnet build Wintime.Connector.UsrModbus.slnx
```
Expected: build succeeds.
```bash
git add -A && git commit -q -m "feat: MqttPublisher (Control telemetry contract)"
```

---

## Task 7: Оркестратор мультиустройства + Program wiring

**Files:**
- Create: `$CONN/Wintime.Connector.UsrModbus/Workers/UsrModbusPollingWorker.cs`
- Modify: `$CONN/Wintime.Connector.UsrModbus/Program.cs`
- Create: `$CONN/Wintime.Connector.UsrModbus/samples/machines.json`
- Create: `$CONN/Wintime.Connector.UsrModbus/samples/config/machine-01.json`

**Interfaces:**
- Consumes: `IMachineSource` (Task 4), `AliasConfigLoader` (Task 5), `MqttPublisher` (Task 6), `StateTelemetryMapper` (Task 3), `ConnectorEngine`/`NModbusReader`/`MachineState` (Core).
- Produces: `Wintime.Connector.UsrModbus.Workers.UsrModbusPollingWorker : BackgroundService`.

- [ ] **Step 1: Реализовать воркер**

Create `$CONN/Wintime.Connector.UsrModbus/Workers/UsrModbusPollingWorker.cs`:
```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Wintime.Connector.UsrModbus.Api;
using Wintime.Connector.UsrModbus.Config;
using Wintime.Connector.UsrModbus.Core;
using Wintime.Connector.UsrModbus.Mapping;
using Wintime.Connector.UsrModbus.Models;
using Wintime.Connector.UsrModbus.Mqtt;

namespace Wintime.Connector.UsrModbus.Workers;

/// <summary>
/// Оркестратор верхнего слоя: тянет список ТПА из Control, поднимает по одному
/// ConnectorEngine на устройство (все опрашиваются одновременно), маппит MachineState
/// в телеметрию и публикует в MQTT. В Offline (mapper вернул null) наружу не шлём.
/// </summary>
public sealed class UsrModbusPollingWorker : BackgroundService
{
    private readonly IMachineSource _machineSource;
    private readonly MqttPublisher _mqtt;
    private readonly ConnectorSettings _settings;
    private readonly ILogger<UsrModbusPollingWorker> _logger;

    public UsrModbusPollingWorker(IMachineSource machineSource, MqttPublisher mqtt,
        ConnectorSettings settings, ILogger<UsrModbusPollingWorker> logger)
    {
        _machineSource = machineSource;
        _mqtt = mqtt;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _mqtt.ConnectAsync(stoppingToken);

        List<MachineDescriptor> machines = new();
        while (!stoppingToken.IsCancellationRequested)
        {
            try { machines = await _machineSource.GetMachinesAsync(stoppingToken); break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load machines, retry in {Delay}ms", _settings.ReconnectDelayMs);
                await Task.Delay(_settings.ReconnectDelayMs, stoppingToken);
            }
        }
        if (stoppingToken.IsCancellationRequested) return;

        var tasks = new List<Task>();
        foreach (var machine in machines)
        {
            if (string.IsNullOrWhiteSpace(machine.ConnectorAlias))
            {
                _logger.LogWarning("[{Name}] no ConnectorAlias — skipped", machine.ImmName);
                continue;
            }
            if (!AliasConfigLoader.TryLoad(_settings.ConfigDir, machine.ConnectorAlias, out var config))
            {
                _logger.LogWarning("[{Name}] config '{Alias}.json' not found in {Dir} — skipped",
                    machine.ImmName, machine.ConnectorAlias, _settings.ConfigDir);
                continue;
            }
            tasks.Add(RunEngineAsync(machine, config!, stoppingToken));
        }

        if (tasks.Count == 0) { _logger.LogWarning("No pollable machines configured."); return; }
        await Task.WhenAll(tasks);
    }

    private Task RunEngineAsync(MachineDescriptor machine, ConnectorConfig config, CancellationToken ct)
    {
        var device = config.Device;
        var engine = new ConnectorEngine(config,
            () => new NModbusReader(device.Host, device.Port, device.TimeoutMs));

        engine.StateUpdated += state =>
        {
            var message = StateTelemetryMapper.Map(state);
            if (message is null) return; // Offline — не публикуем
            _ = PublishSafeAsync(machine, message, state.TimestampUtc, ct);
        };

        _logger.LogInformation("[{Name}] polling {Host}:{Port} (unit {Unit}) every {Poll}ms",
            machine.ImmName, device.Host, device.Port, device.UnitId, device.PollIntervalMs);

        return engine.RunAsync(ct);
    }

    private async Task PublishSafeAsync(MachineDescriptor machine, TelemetryMessage message,
        DateTimeOffset ts, CancellationToken ct)
    {
        try { await _mqtt.PublishTelemetryAsync(machine.ImmId, message, ts, ct); }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "[{Name}] publish failed", machine.ImmName);
        }
    }
}
```

- [ ] **Step 2: Дописать Program.cs (DI: источник, паблишер, воркер)**

Replace `$CONN/Wintime.Connector.UsrModbus/Program.cs` полностью:
```csharp
using Wintime.Connector.UsrModbus.Api;
using Wintime.Connector.UsrModbus.Models;
using Wintime.Connector.UsrModbus.Mqtt;
using Wintime.Connector.UsrModbus.Workers;

var builder = Host.CreateApplicationBuilder(args);

var cfg = builder.Configuration;
var mqttSettings      = cfg.GetSection("Mqtt").Get<MqttSettings>()           ?? new MqttSettings();
var connectorSettings = cfg.GetSection("Connector").Get<ConnectorSettings>() ?? new ConnectorSettings();

builder.Services.AddSingleton(mqttSettings);
builder.Services.AddSingleton(connectorSettings);

if (connectorSettings.Source.Equals("file", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IMachineSource, FileMachineSource>();
else
    builder.Services.AddSingleton<IMachineSource, ControlApiClient>();

builder.Services.AddSingleton<MqttPublisher>();
builder.Services.AddHostedService<UsrModbusPollingWorker>();

var host = builder.Build();
host.Run();
```

- [ ] **Step 3: Примеры конфигурации**

Create `$CONN/Wintime.Connector.UsrModbus/samples/machines.json`:
```json
[
  { "immId": "11111111-1111-1111-1111-111111111111", "immName": "BM180-MT #1", "connectorAlias": "machine-01" }
]
```

Create `$CONN/Wintime.Connector.UsrModbus/samples/config/machine-01.json` (формат ядра; см. `samples/connector.json` в code_new):
```json
{
  "device": { "host": "192.168.0.7", "port": 502, "unitId": 17, "pollIntervalMs": 500, "timeoutMs": 1000 },
  "profile": "singleNode",
  "stateMachine": { "seedCycleMs": 300000, "alarmTimeoutCoef": 2.0, "idleTimeoutCoef": 3.0, "averageWindowCycles": 10, "offlineAfterFailedPolls": 3 },
  "registers": [
    { "name": "Injection",         "address": "0x0020", "access": "discreteInput", "role": "injection" },
    { "name": "EjectorFwdReached", "address": "0x0021", "access": "discreteInput", "role": "ejectorFwdReached" },
    { "name": "Reject",            "address": "0x0022", "access": "discreteInput", "role": "reject" },
    { "name": "MouldClosed",       "address": "0x0023", "access": "discreteInput", "role": "mouldClosed" },
    { "name": "InjectionPosition", "address": "0x0058", "access": "inputRegister", "role": "injectionPosition", "scale": 0.0225, "unit": "mm" },
    { "name": "MoldPosition",      "address": "0x0059", "access": "inputRegister", "role": "moldPosition",      "scale": 0.045,  "unit": "mm" }
  ]
}
```

Добавить копирование samples в вывод — в csproj Host, `<ItemGroup>` с `None Update`:
```xml
<None Update="samples/**/*.json"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>
```

- [ ] **Step 4: Собрать всё решение**

Run:
```bash
cd "e:/Projects/Control/Sources/Connectors/Wintime.Connector.UsrModbus"
dotnet build Wintime.Connector.UsrModbus.slnx
dotnet test  Wintime.Connector.UsrModbus.slnx
```
Expected: build succeeds; все тесты (ядро + Host-слой) PASS.

- [ ] **Step 5: Ручной smoke-тест (Development/file)**

Требует запущенного MQTT-брокера и хотя бы одного достижимого USR (или Modbus-эмулятора) на `192.168.0.7:502`. Если железа нет — пропустить, отметив это.
Run:
```bash
cd "e:/Projects/Control/Sources/Connectors/Wintime.Connector.UsrModbus/Wintime.Connector.UsrModbus"
DOTNET_ENVIRONMENT=Development dotnet run
```
Expected: в логах `MQTT connected...`, `[BM180-MT #1] polling 192.168.0.7:502 ...`; при живом устройстве в брокере появляются сообщения в `control/imm/11111111-.../telemetry`. Проверить подписчиком (напр. `mosquitto_sub -t 'control/imm/#' -v`): payload содержит `timestamp`, `mode`, `sensors.cycleCounter`.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -q -m "feat: multi-device polling worker + DI wiring + samples"
```

---

## Task 8: Docker и документация коннектора

**Files:**
- Create: `$CONN/Dockerfile`
- Create: `$CONN/.dockerignore`
- Modify: `$CONN/CLAUDE.md` (дописать Host-слой и запуск)

**Interfaces:** нет (упаковка).

- [ ] **Step 1: Dockerfile (multi-stage, кроссплатформенно)**

Create `$CONN/Dockerfile`:
```dockerfile
# --- build ---
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish Wintime.Connector.UsrModbus/Wintime.Connector.UsrModbus.csproj \
    -c Release -o /app

# --- runtime ---
FROM mcr.microsoft.com/dotnet/runtime:9.0
WORKDIR /app
COPY --from=build /app ./
# Конфигурации устройств и список ТПА монтируются volume'ом в /app/config и /app/appsettings.*.
# Секреты (ApiKey, брокер) — через переменные окружения Connector__ApiKey, Mqtt__BrokerHost и т.п.
ENTRYPOINT ["dotnet", "Wintime.Connector.UsrModbus.dll"]
```

Create `$CONN/.dockerignore`:
```
**/bin
**/obj
.git
```

- [ ] **Step 2: Проверить сборку образа**

Run:
```bash
cd "e:/Projects/Control/Sources/Connectors/Wintime.Connector.UsrModbus"
docker build -t wintime-connector-usrmodbus:dev .
```
Expected: образ собирается без ошибок. Если Docker недоступен в среде — вместо этого проверить публикацию: `dotnet publish Wintime.Connector.UsrModbus/Wintime.Connector.UsrModbus.csproj -c Release -o /tmp/app` (успешно).

- [ ] **Step 3: Дописать CLAUDE.md (Host-слой)**

Добавить в конец `$CONN/CLAUDE.md` раздел:
```markdown
## Host-слой (Wintime.Connector.UsrModbus)

Верхний слой над ядром: интеграция с Wintime Control.
- Источник ТПА: `IMachineSource` — `ControlApiClient` (GET /api/connectors/usr-modbus/machines,
  X-Api-Key) или `FileMachineSource` (Source="file").
- Связка: `ConnectorAlias` → `config/<alias>.json` (формат ядра) через `AliasConfigLoader`.
- На каждый ТПА — свой `ConnectorEngine`; все опрашиваются одновременно (`Task.WhenAll`).
- `StateTelemetryMapper`: `MachineState` → `{mode, sensors}`; `Offline` → не публикуем
  (Control выведет offline по таймауту). Имена сенсоров = `ParameterName` шаблона ТПА;
  счётчик циклов = `cycleCounter`.
- `MqttPublisher`: топик `control/imm/{immId}/telemetry`, payload `{timestamp, mode, sensors}`.

Запуск: `dotnet run` (env `DOTNET_ENVIRONMENT=Development` → Source=file, samples).
Контейнер: `docker build -t wintime-connector-usrmodbus .`; конфиг устройств — volume в /app/config,
секреты — env (`Connector__ApiKey`, `Mqtt__BrokerHost`).
```

- [ ] **Step 4: Финальная сборка/тесты, commit**

Run:
```bash
cd "e:/Projects/Control/Sources/Connectors/Wintime.Connector.UsrModbus"
dotnet build Wintime.Connector.UsrModbus.slnx && dotnet test Wintime.Connector.UsrModbus.slnx
```
Expected: зелёные.
```bash
git add -A && git commit -q -m "chore: Dockerfile + connector docs"
```

---

## Итог

После задачи 8: кроссплатформенный коннектор `Wintime.Connector.UsrModbus` — переиспользует замороженное ядро, публикует состояние множества ТПА в контракт телеметрии Control, конфигурируется каталогом `config/<alias>.json` и `appsettings`/env, собирается в Docker-образ. Все чистые единицы (маппер, парсер, загрузчик) покрыты юнит-тестами; воркер/паблишер проверяются сборкой и ручным smoke-тестом. **Подзадача 2 (Тестер/калибровка)** — отдельным планом.
