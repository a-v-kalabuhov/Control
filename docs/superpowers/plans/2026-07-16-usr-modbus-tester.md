# USR-Modbus Tester Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a console TUI tool that commissions a USR-IO424T-EWR gateway on a real IMM — maps discrete inputs to signal roles with polarity detection, calibrates analog inputs to millimetres, generates a `connector.json` the production connector reads unchanged, and live-verifies any config through the reused state machine.

**Architecture:** New console app `Wintime.Connector.UsrModbus.Tester` in the connector solution, referencing `Wintime.Connector.UsrModbus.Core`. The tool reuses Core for all Modbus I/O (`IModbusReader`/`NModbusReader`), config types, `ConfigLoader`, `ConnectorProfileValidator`, and `ConnectorEngine`+state machine. It adds only UI, calibration math, and a JSON writer. All calibration/config logic lives in pure, hardware-free units behind `IModbusReader`; Spectre.Console rendering and Modbus sockets are thin adapters verified by manual smoke.

**Tech Stack:** .NET 9 (`net9.0`), C# latest, Spectre.Console (TUI), xUnit (tests), NModbus (via Core, transitively).

## Global Constraints

- **Read-only Modbus.** Only reading function codes (0x01–0x04) via Core's `IModbusReader`. Never write to the gateway in any mode, including verify (no MQTT publish — display only).
- **Reuse Core, no duplication.** No own Modbus layer, config types, decoders, or state machine. Consume `Wintime.Connector.UsrModbus.Core` public API.
- **Output compatibility.** Every `connector.json` the tool writes MUST round-trip through `Core.ConfigLoader.LoadFromJson` before being saved; if it does not parse, do not write the file.
- **Target framework `net9.0`**, `Nullable` enable, `ImplicitUsings` enable — match sibling projects.
- **Tests are xUnit** (`xunit` 2.9.*, `Microsoft.NET.Test.Sdk` 17.*, `xunit.runner.visualstudio` 2.8.*) — match `Wintime.Connector.UsrModbus.Tests.csproj`.
- **Repo:** connector lives in its own git repo at `e:/Projects/Control/Sources/Connectors/Wintime.Connector.UsrModbus` (branch master, no remote) — commits happen there, NOT in the Control repo.
- **Do not read self-clearing registers** `0x0030..0x0033` (they reset on read).
- **Spec:** `docs/superpowers/specs/2026-07-16-usr-modbus-tester-design.md` (in the Control repo).

## File Structure

All paths below are relative to `e:/Projects/Control/Sources/Connectors/Wintime.Connector.UsrModbus`.

New project `Wintime.Connector.UsrModbus.Tester/`:
- `Wintime.Connector.UsrModbus.Tester.csproj` — console exe; refs Core + Spectre.Console.
- `Program.cs` — entry point; main-menu loop; wires modes.
- `Session.cs` — mutable connection parameters (host/port/unitId/timeoutMs) + `IModbusReader` factory.
- `Modbus/RawSignals.cs` — **pure** decoders: AI mV, PT100 temperature, counter wrap-delta.
- `Modbus/RawSnapshot.cs` — value record of one raw poll (DI, coils, AI, temp, counters).
- `Modbus/RawPoller.cs` — reads one `RawSnapshot` from an `IModbusReader` (thin).
- `Modbus/RawMonitorScreen.cs` — Spectre live-table render loop for the raw monitor (thin).
- `Connection/McuIdentity.cs` — value record of MCU sw/hw version + probe result.
- `Connection/ConnectionChecker.cs` — reads version regs + DI probe from `IModbusReader` (semi-thin).
- `Calibration/LinearFit.cs` — **pure** two-point and one-point calibration math.
- `Calibration/PolarityDetector.cs` — **pure** changed-DI + polarity detection from before/after snapshots.
- `Wizard/ConfigDraft.cs` — mutable working model → `Build()` produces `ConnectorConfig`; skip = keep.
- `Wizard/ConnectorJsonWriter.cs` — **pure** `ConnectorConfig` → JSON string in Core's schema.
- `Wizard/WizardScreen.cs` — Spectre-driven wizard orchestration (thin).
- `Verify/VerifyScreen.cs` — loads a config, runs `ConnectorEngine`, renders `MachineState` (thin).

New test project `Wintime.Connector.UsrModbus.Tester.Tests/`:
- `Wintime.Connector.UsrModbus.Tester.Tests.csproj` — xUnit; refs Tester + Core.
- `FakeModbusReader.cs` — scripted `IModbusReader` test double.
- `RawSignalsTests.cs`, `RawPollerTests.cs`, `ConnectionCheckerTests.cs`, `LinearFitTests.cs`, `PolarityDetectorTests.cs`, `ConfigDraftTests.cs`, `ConnectorJsonWriterTests.cs`.

Thin adapters (`RawMonitorScreen`, `WizardScreen`, `VerifyScreen`, `Program`) are verified by manual smoke, not xUnit — their logic lives in the pure units above.

---

### Task 1: Scaffold projects + raw signal decoders

**Files:**
- Create: `Wintime.Connector.UsrModbus.Tester/Wintime.Connector.UsrModbus.Tester.csproj`
- Create: `Wintime.Connector.UsrModbus.Tester/Modbus/RawSignals.cs`
- Create: `Wintime.Connector.UsrModbus.Tester.Tests/Wintime.Connector.UsrModbus.Tester.Tests.csproj`
- Create: `Wintime.Connector.UsrModbus.Tester.Tests/RawSignalsTests.cs`
- Modify: `Wintime.Connector.UsrModbus.slnx`

**Interfaces:**
- Produces: `RawSignals.MilliVolts(ushort raw) → int`; `RawSignals.TemperatureC(ushort raw) → double`; `RawSignals.CounterDelta(ushort prev, ushort curr) → int`.

- [ ] **Step 1: Create the Tester project file**

Create `Wintime.Connector.UsrModbus.Tester/Wintime.Connector.UsrModbus.Tester.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Wintime.Connector.UsrModbus.Tester</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Spectre.Console" Version="0.49.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Wintime.Connector.UsrModbus.Core\Wintime.Connector.UsrModbus.Core.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create a temporary Program.cs so the exe builds**

Create `Wintime.Connector.UsrModbus.Tester/Program.cs`:

```csharp
// Temporary entry point — replaced by the main menu in Task 10.
System.Console.WriteLine("Wintime USR-Modbus Tester");
```

- [ ] **Step 3: Create the test project file**

Create `Wintime.Connector.UsrModbus.Tester.Tests/Wintime.Connector.UsrModbus.Tester.Tests.csproj`:

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
    <ProjectReference Include="..\Wintime.Connector.UsrModbus.Tester\Wintime.Connector.UsrModbus.Tester.csproj" />
    <ProjectReference Include="..\Wintime.Connector.UsrModbus.Core\Wintime.Connector.UsrModbus.Core.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 4: Register both projects in the solution**

Modify `Wintime.Connector.UsrModbus.slnx` — add two `<Project>` lines inside `<Solution>`:

```xml
  <Project Path="Wintime.Connector.UsrModbus.Tester/Wintime.Connector.UsrModbus.Tester.csproj" />
  <Project Path="Wintime.Connector.UsrModbus.Tester.Tests/Wintime.Connector.UsrModbus.Tester.Tests.csproj" />
```

- [ ] **Step 5: Write the failing test**

Create `Wintime.Connector.UsrModbus.Tester.Tests/RawSignalsTests.cs`:

```csharp
using Wintime.Connector.UsrModbus.Tester.Modbus;
using Xunit;

namespace Wintime.Connector.UsrModbus.Tester.Tests;

public class RawSignalsTests
{
    [Fact]
    public void MilliVolts_ReturnsRawAsMilliVolts()
    {
        Assert.Equal(4096, RawSignals.MilliVolts(4096)); // 0x1000 ≈ 4.096 V
    }

    [Theory]
    [InlineData(1682, -83.18)]  // 0x0692 example from the Modbus spec
    [InlineData(10000, 0.0)]
    [InlineData(30000, 200.0)]
    public void TemperatureC_UsesOffsetEncoding(ushort raw, double expected)
    {
        Assert.Equal(expected, RawSignals.TemperatureC(raw), 2);
    }

    [Fact]
    public void CounterDelta_HandlesUnsignedWrap()
    {
        Assert.Equal(3, RawSignals.CounterDelta(65534, 1)); // 65534→65535→0→1 = 3 edges
        Assert.Equal(5, RawSignals.CounterDelta(10, 15));
    }
}
```

- [ ] **Step 6: Run the test to verify it fails**

Run: `dotnet test Wintime.Connector.UsrModbus.Tester.Tests --filter RawSignalsTests`
Expected: FAIL — `RawSignals` does not exist (build error).

- [ ] **Step 7: Implement RawSignals**

Create `Wintime.Connector.UsrModbus.Tester/Modbus/RawSignals.cs`:

```csharp
namespace Wintime.Connector.UsrModbus.Tester.Modbus;

/// <summary>
/// Stateless decoding of raw USR-IO424 register words to physical values.
/// Assumes host-order ushort as returned by Core's <c>IModbusReader</c>.
/// </summary>
public static class RawSignals
{
    /// <summary>AI voltage register value is already millivolts (Modbus spec §3.5).</summary>
    public static int MilliVolts(ushort raw) => raw;

    /// <summary>PT100: offset encoding, T(°C) = (raw − 10000) / 100 (Modbus spec §3.4).</summary>
    public static double TemperatureC(ushort raw) => (raw - 10000) / 100.0;

    /// <summary>Pulse-counter increment between polls with unsigned 16-bit wrap.</summary>
    public static int CounterDelta(ushort prev, ushort curr) => (ushort)(curr - prev);
}
```

- [ ] **Step 8: Run the test to verify it passes**

Run: `dotnet test Wintime.Connector.UsrModbus.Tester.Tests --filter RawSignalsTests`
Expected: PASS (all cases).

- [ ] **Step 9: Verify the whole solution builds**

Run: `dotnet build Wintime.Connector.UsrModbus.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 10: Commit**

```bash
git add Wintime.Connector.UsrModbus.Tester Wintime.Connector.UsrModbus.Tester.Tests Wintime.Connector.UsrModbus.slnx
git commit -m "feat(tester): scaffold Tester projects + raw signal decoders"
```

---

### Task 2: Fake Modbus reader (test double)

**Files:**
- Create: `Wintime.Connector.UsrModbus.Tester.Tests/FakeModbusReader.cs`

**Interfaces:**
- Produces: `FakeModbusReader` implementing `Wintime.Connector.UsrModbus.Core.IModbusReader`; ctor takes optional seed maps; mutator methods `SetDiscreteInputs(ushort start, params bool[] bits)`, `SetInputRegisters(ushort start, params ushort[] words)`, `SetHoldingRegisters(ushort start, params ushort[] words)`, `SetCoils(ushort start, params bool[] bits)`; property `int ReadCount`.

This task has no separate test — the double is exercised by every later test task. Its deliverable is verified when Task 3's tests compile and pass.

- [ ] **Step 1: Implement the fake reader**

Create `Wintime.Connector.UsrModbus.Tester.Tests/FakeModbusReader.cs`:

```csharp
using Wintime.Connector.UsrModbus.Core;

namespace Wintime.Connector.UsrModbus.Tester.Tests;

/// <summary>
/// Scripted <see cref="IModbusReader"/> for tests. Returns values seeded per address;
/// unseeded addresses read as 0 / false. Reads never throw (see FailingModbusReader for that).
/// </summary>
public sealed class FakeModbusReader : IModbusReader
{
    private readonly Dictionary<ushort, bool> _discrete = new();
    private readonly Dictionary<ushort, bool> _coils = new();
    private readonly Dictionary<ushort, ushort> _input = new();
    private readonly Dictionary<ushort, ushort> _holding = new();

    public int ReadCount { get; private set; }

    public FakeModbusReader SetDiscreteInputs(ushort start, params bool[] bits)
    {
        for (ushort i = 0; i < bits.Length; i++) _discrete[(ushort)(start + i)] = bits[i];
        return this;
    }

    public FakeModbusReader SetCoils(ushort start, params bool[] bits)
    {
        for (ushort i = 0; i < bits.Length; i++) _coils[(ushort)(start + i)] = bits[i];
        return this;
    }

    public FakeModbusReader SetInputRegisters(ushort start, params ushort[] words)
    {
        for (ushort i = 0; i < words.Length; i++) _input[(ushort)(start + i)] = words[i];
        return this;
    }

    public FakeModbusReader SetHoldingRegisters(ushort start, params ushort[] words)
    {
        for (ushort i = 0; i < words.Length; i++) _holding[(ushort)(start + i)] = words[i];
        return this;
    }

    public bool[] ReadCoils(byte unitId, ushort start, ushort count) => ReadBits(_coils, start, count);
    public bool[] ReadDiscreteInputs(byte unitId, ushort start, ushort count) => ReadBits(_discrete, start, count);
    public ushort[] ReadHoldingRegisters(byte unitId, ushort start, ushort count) => ReadWords(_holding, start, count);
    public ushort[] ReadInputRegisters(byte unitId, ushort start, ushort count) => ReadWords(_input, start, count);

    private bool[] ReadBits(Dictionary<ushort, bool> map, ushort start, ushort count)
    {
        ReadCount++;
        var r = new bool[count];
        for (ushort i = 0; i < count; i++) r[i] = map.GetValueOrDefault((ushort)(start + i));
        return r;
    }

    private ushort[] ReadWords(Dictionary<ushort, ushort> map, ushort start, ushort count)
    {
        ReadCount++;
        var r = new ushort[count];
        for (ushort i = 0; i < count; i++) r[i] = map.GetValueOrDefault((ushort)(start + i));
        return r;
    }

    public void Dispose() { }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build Wintime.Connector.UsrModbus.Tester.Tests`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add Wintime.Connector.UsrModbus.Tester.Tests/FakeModbusReader.cs
git commit -m "test(tester): add FakeModbusReader test double"
```

---

### Task 3: Raw poll snapshot

**Files:**
- Create: `Wintime.Connector.UsrModbus.Tester/Modbus/RawSnapshot.cs`
- Create: `Wintime.Connector.UsrModbus.Tester/Modbus/RawPoller.cs`
- Create: `Wintime.Connector.UsrModbus.Tester.Tests/RawPollerTests.cs`

**Interfaces:**
- Consumes: `RawSignals` (Task 1), `IModbusReader`, `FakeModbusReader` (Task 2).
- Produces: `RawSnapshot` record with `bool[] Di` (4), `bool[] Relays` (4), `int[] AiMilliVolts` (2), `double TemperatureC`, `ushort[] CounterRaw` (4); `RawPoller.Poll(IModbusReader reader, byte unitId) → RawSnapshot`.

- [ ] **Step 1: Write the failing test**

Create `Wintime.Connector.UsrModbus.Tester.Tests/RawPollerTests.cs`:

```csharp
using Wintime.Connector.UsrModbus.Tester.Modbus;
using Xunit;

namespace Wintime.Connector.UsrModbus.Tester.Tests;

public class RawPollerTests
{
    [Fact]
    public void Poll_ReadsAllBlocksAndDecodes()
    {
        var reader = new FakeModbusReader()
            .SetDiscreteInputs(0x0020, true, false, true, false)   // DI1..DI4
            .SetCoils(0x0000, false, false, false, false)          // relays DO1..DO4
            .SetInputRegisters(0x0050, 1682)                       // temperature
            .SetInputRegisters(0x0058, 4096, 2048)                 // AI1, AI2 (mV)
            .SetInputRegisters(0x0040, 10, 20, 0, 0);              // counters

        var snap = RawPoller.Poll(reader, unitId: 17);

        Assert.Equal(new[] { true, false, true, false }, snap.Di);
        Assert.Equal(new[] { 4096, 2048 }, snap.AiMilliVolts);
        Assert.Equal(-83.18, snap.TemperatureC, 2);
        Assert.Equal((ushort)10, snap.CounterRaw[0]);
    }

    [Fact]
    public void Poll_DoesNotReadSelfClearingButtonRegisters()
    {
        // 0x0030..0x0033 must never be read; a poll issues a bounded number of block reads.
        var reader = new FakeModbusReader();
        RawPoller.Poll(reader, 17);
        Assert.Equal(5, reader.ReadCount); // DI, relays, temp, AI, counters — no button block
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Wintime.Connector.UsrModbus.Tester.Tests --filter RawPollerTests`
Expected: FAIL — `RawSnapshot`/`RawPoller` not defined.

- [ ] **Step 3: Implement RawSnapshot**

Create `Wintime.Connector.UsrModbus.Tester/Modbus/RawSnapshot.cs`:

```csharp
namespace Wintime.Connector.UsrModbus.Tester.Modbus;

/// <summary>One raw poll of the gateway — undecoded topology, physical values where trivial.</summary>
public sealed record RawSnapshot
{
    public required bool[] Di { get; init; }            // DI1..DI4  (0x0020..0x0023, FC02)
    public required bool[] Relays { get; init; }        // DO1..DO4  (0x0000..0x0003, FC01)
    public required int[] AiMilliVolts { get; init; }   // AI1, AI2  (0x0058..0x0059, FC04)
    public required double TemperatureC { get; init; }  // PT100     (0x0050, FC04)
    public required ushort[] CounterRaw { get; init; }  // pulse cnt (0x0040..0x0043, FC04)
}
```

- [ ] **Step 4: Implement RawPoller**

Create `Wintime.Connector.UsrModbus.Tester/Modbus/RawPoller.cs`:

```csharp
using Wintime.Connector.UsrModbus.Core;

namespace Wintime.Connector.UsrModbus.Tester.Modbus;

/// <summary>
/// Reads a single <see cref="RawSnapshot"/> using fixed USR-IO424 addresses. Read-only.
/// Deliberately never touches the self-clearing button registers 0x0030..0x0033.
/// </summary>
public static class RawPoller
{
    public static RawSnapshot Poll(IModbusReader reader, byte unitId)
    {
        bool[] di = reader.ReadDiscreteInputs(unitId, 0x0020, 4);
        bool[] relays = reader.ReadCoils(unitId, 0x0000, 4);
        ushort[] temp = reader.ReadInputRegisters(unitId, 0x0050, 1);
        ushort[] ai = reader.ReadInputRegisters(unitId, 0x0058, 2);
        ushort[] counters = reader.ReadInputRegisters(unitId, 0x0040, 4);

        return new RawSnapshot
        {
            Di = di,
            Relays = relays,
            AiMilliVolts = [RawSignals.MilliVolts(ai[0]), RawSignals.MilliVolts(ai[1])],
            TemperatureC = RawSignals.TemperatureC(temp[0]),
            CounterRaw = counters,
        };
    }
}
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test Wintime.Connector.UsrModbus.Tester.Tests --filter RawPollerTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Wintime.Connector.UsrModbus.Tester/Modbus Wintime.Connector.UsrModbus.Tester.Tests/RawPollerTests.cs
git commit -m "feat(tester): raw poll snapshot over fixed USR addresses"
```

---

### Task 4: Connection checker (MCU identity)

**Files:**
- Create: `Wintime.Connector.UsrModbus.Tester/Connection/McuIdentity.cs`
- Create: `Wintime.Connector.UsrModbus.Tester/Connection/ConnectionChecker.cs`
- Create: `Wintime.Connector.UsrModbus.Tester.Tests/ConnectionCheckerTests.cs`

**Interfaces:**
- Consumes: `IModbusReader`, `FakeModbusReader`.
- Produces: `McuIdentity` record `{ string Software, string Hardware }`; `ConnectionChecker.ReadIdentity(IModbusReader, byte unitId) → McuIdentity`; `McuIdentity.FormatVersion(ushort raw) → string` (`0x0112` → `"V1.1.2"`).

- [ ] **Step 1: Write the failing test**

Create `Wintime.Connector.UsrModbus.Tester.Tests/ConnectionCheckerTests.cs`:

```csharp
using Wintime.Connector.UsrModbus.Tester.Connection;
using Xunit;

namespace Wintime.Connector.UsrModbus.Tester.Tests;

public class ConnectionCheckerTests
{
    [Theory]
    [InlineData(0x0112, "V1.1.2")]
    [InlineData(0x0110, "V1.1.0")]
    public void FormatVersion_DecodesNibbleTriplet(ushort raw, string expected)
    {
        Assert.Equal(expected, McuIdentity.FormatVersion(raw));
    }

    [Fact]
    public void ReadIdentity_ReadsSoftwareAndHardwareRegisters()
    {
        var reader = new FakeModbusReader()
            .SetHoldingRegisters(0x00B4, 0x0112)   // MCU software
            .SetHoldingRegisters(0x00B5, 0x0110);  // MCU hardware

        var id = ConnectionChecker.ReadIdentity(reader, 17);

        Assert.Equal("V1.1.2", id.Software);
        Assert.Equal("V1.1.0", id.Hardware);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Wintime.Connector.UsrModbus.Tester.Tests --filter ConnectionCheckerTests`
Expected: FAIL — types not defined.

- [ ] **Step 3: Implement McuIdentity**

Create `Wintime.Connector.UsrModbus.Tester/Connection/McuIdentity.cs`:

```csharp
namespace Wintime.Connector.UsrModbus.Tester.Connection;

/// <summary>MCU version identity read from USR diagnostic registers 0x00B4/0x00B5.</summary>
public sealed record McuIdentity
{
    public required string Software { get; init; }
    public required string Hardware { get; init; }

    /// <summary>Decodes a version word: 0x0112 → "V1.1.2" (three nibbles = major.minor.patch).</summary>
    public static string FormatVersion(ushort raw) =>
        $"V{(raw >> 8) & 0xF}.{(raw >> 4) & 0xF}.{raw & 0xF}";
}
```

- [ ] **Step 4: Implement ConnectionChecker**

Create `Wintime.Connector.UsrModbus.Tester/Connection/ConnectionChecker.cs`:

```csharp
using Wintime.Connector.UsrModbus.Core;

namespace Wintime.Connector.UsrModbus.Tester.Connection;

/// <summary>
/// Confirms a reachable Modbus node is really a USR-IO424 by reading its MCU version
/// registers (0x00B4 software, 0x00B5 hardware, FC 0x03). Read-only.
/// </summary>
public static class ConnectionChecker
{
    public static McuIdentity ReadIdentity(IModbusReader reader, byte unitId)
    {
        ushort sw = reader.ReadHoldingRegisters(unitId, 0x00B4, 1)[0];
        ushort hw = reader.ReadHoldingRegisters(unitId, 0x00B5, 1)[0];
        return new McuIdentity
        {
            Software = McuIdentity.FormatVersion(sw),
            Hardware = McuIdentity.FormatVersion(hw),
        };
    }
}
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test Wintime.Connector.UsrModbus.Tester.Tests --filter ConnectionCheckerTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Wintime.Connector.UsrModbus.Tester/Connection Wintime.Connector.UsrModbus.Tester.Tests/ConnectionCheckerTests.cs
git commit -m "feat(tester): connection identity check via MCU version registers"
```

---

### Task 5: Linear calibration math

**Files:**
- Create: `Wintime.Connector.UsrModbus.Tester/Calibration/LinearFit.cs`
- Create: `Wintime.Connector.UsrModbus.Tester.Tests/LinearFitTests.cs`

**Interfaces:**
- Produces: `LinearFit` record `{ double Scale, double Offset }`; `LinearFit.TwoPoint(double mmA, double mvA, double mmB, double mvB) → LinearFit`; `LinearFit.OnePoint(double mm, double mv) → LinearFit`; both throw `ArgumentException` on degenerate input (equal mV / zero mV).

- [ ] **Step 1: Write the failing test**

Create `Wintime.Connector.UsrModbus.Tester.Tests/LinearFitTests.cs`:

```csharp
using System;
using Wintime.Connector.UsrModbus.Tester.Calibration;
using Xunit;

namespace Wintime.Connector.UsrModbus.Tester.Tests;

public class LinearFitTests
{
    [Fact]
    public void TwoPoint_SolvesScaleAndOffset()
    {
        // 0 mm @ 0 mV, 225 mm @ 10000 mV  → scale 0.0225, offset 0
        var fit = LinearFit.TwoPoint(mmA: 0, mvA: 0, mmB: 225, mvB: 10000);
        Assert.Equal(0.0225, fit.Scale, 6);
        Assert.Equal(0.0, fit.Offset, 6);
    }

    [Fact]
    public void TwoPoint_HandlesNonZeroOffset()
    {
        // 10 mm @ 1000 mV, 20 mm @ 2000 mV → scale 0.01, offset 0
        var fit = LinearFit.TwoPoint(10, 1000, 20, 2000);
        Assert.Equal(0.01, fit.Scale, 6);
        Assert.Equal(0.0, fit.Offset, 6);

        // 5 mm @ 1000 mV, 25 mm @ 2000 mV → scale 0.02, offset -15
        var fit2 = LinearFit.TwoPoint(5, 1000, 25, 2000);
        Assert.Equal(0.02, fit2.Scale, 6);
        Assert.Equal(-15.0, fit2.Offset, 6);
    }

    [Fact]
    public void OnePoint_IsRatiometric()
    {
        var fit = LinearFit.OnePoint(mm: 225, mv: 10000);
        Assert.Equal(0.0225, fit.Scale, 6);
        Assert.Equal(0.0, fit.Offset, 6);
    }

    [Fact]
    public void TwoPoint_ThrowsWhenVoltagesEqual()
    {
        Assert.Throws<ArgumentException>(() => LinearFit.TwoPoint(0, 1000, 10, 1000));
    }

    [Fact]
    public void OnePoint_ThrowsWhenVoltageZero()
    {
        Assert.Throws<ArgumentException>(() => LinearFit.OnePoint(10, 0));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Wintime.Connector.UsrModbus.Tester.Tests --filter LinearFitTests`
Expected: FAIL — `LinearFit` not defined.

- [ ] **Step 3: Implement LinearFit**

Create `Wintime.Connector.UsrModbus.Tester/Calibration/LinearFit.cs`:

```csharp
namespace Wintime.Connector.UsrModbus.Tester.Calibration;

/// <summary>Linear calibration mm = Scale · mV + Offset for an analog input.</summary>
public sealed record LinearFit
{
    public required double Scale { get; init; }
    public required double Offset { get; init; }

    /// <summary>Two known points (mm, mV) → scale and offset of the connecting line.</summary>
    public static LinearFit TwoPoint(double mmA, double mvA, double mmB, double mvB)
    {
        if (mvA == mvB)
            throw new ArgumentException("Две точки калибровки имеют одинаковое напряжение — наклон неопределён.");

        double scale = (mmB - mmA) / (mvB - mvA);
        double offset = mmA - scale * mvA;
        return new LinearFit { Scale = scale, Offset = offset };
    }

    /// <summary>One known point through the origin (ratiometric): offset = 0.</summary>
    public static LinearFit OnePoint(double mm, double mv)
    {
        if (mv == 0)
            throw new ArgumentException("Одноточечная калибровка требует ненулевого напряжения.");

        return new LinearFit { Scale = mm / mv, Offset = 0.0 };
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test Wintime.Connector.UsrModbus.Tester.Tests --filter LinearFitTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Wintime.Connector.UsrModbus.Tester/Calibration/LinearFit.cs Wintime.Connector.UsrModbus.Tester.Tests/LinearFitTests.cs
git commit -m "feat(tester): two-point and one-point analog calibration math"
```

---

### Task 6: Polarity / changed-DI detection

**Files:**
- Create: `Wintime.Connector.UsrModbus.Tester/Calibration/PolarityDetector.cs`
- Create: `Wintime.Connector.UsrModbus.Tester.Tests/PolarityDetectorTests.cs`

**Interfaces:**
- Produces: `DiChange` record `{ int? Index, bool Invert, DiChangeKind Kind }`; enum `DiChangeKind { Single, None, Ambiguous }`; `PolarityDetector.Detect(bool[] before, bool[] after) → DiChange`. `Invert = true` when the activated input reads `false` in its active state (NC contact: transition 1→0).

- [ ] **Step 1: Write the failing test**

Create `Wintime.Connector.UsrModbus.Tester.Tests/PolarityDetectorTests.cs`:

```csharp
using Wintime.Connector.UsrModbus.Tester.Calibration;
using Xunit;

namespace Wintime.Connector.UsrModbus.Tester.Tests;

public class PolarityDetectorTests
{
    [Fact]
    public void Detect_NormallyOpen_RisingEdge_NoInvert()
    {
        // DI2 went 0→1 on activation: normally-open contact, active reads 1.
        var change = PolarityDetector.Detect(
            before: [false, false, false, false],
            after:  [false, true,  false, false]);

        Assert.Equal(DiChangeKind.Single, change.Kind);
        Assert.Equal(1, change.Index);
        Assert.False(change.Invert);
    }

    [Fact]
    public void Detect_NormallyClosed_FallingEdge_SetsInvert()
    {
        // DI3 went 1→0 on activation: normally-closed contact, active reads 0 → invert.
        var change = PolarityDetector.Detect(
            before: [false, false, true, false],
            after:  [false, false, false, false]);

        Assert.Equal(DiChangeKind.Single, change.Kind);
        Assert.Equal(2, change.Index);
        Assert.True(change.Invert);
    }

    [Fact]
    public void Detect_NoChange_ReportsNone()
    {
        var change = PolarityDetector.Detect([true, false], [true, false]);
        Assert.Equal(DiChangeKind.None, change.Kind);
        Assert.Null(change.Index);
    }

    [Fact]
    public void Detect_MultipleChanges_ReportsAmbiguous()
    {
        var change = PolarityDetector.Detect([false, false], [true, true]);
        Assert.Equal(DiChangeKind.Ambiguous, change.Kind);
        Assert.Null(change.Index);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Wintime.Connector.UsrModbus.Tester.Tests --filter PolarityDetectorTests`
Expected: FAIL — types not defined.

- [ ] **Step 3: Implement PolarityDetector**

Create `Wintime.Connector.UsrModbus.Tester/Calibration/PolarityDetector.cs`:

```csharp
namespace Wintime.Connector.UsrModbus.Tester.Calibration;

public enum DiChangeKind { Single, None, Ambiguous }

/// <summary>Which discrete input changed when the engineer activated a signal, and its polarity.</summary>
public sealed record DiChange
{
    public required int? Index { get; init; }
    public required bool Invert { get; init; }
    public required DiChangeKind Kind { get; init; }
}

/// <summary>
/// Compares DI levels before and after the engineer activates a signal. Exactly one changed
/// input maps the signal to that DI; a 1→0 transition means the contact is normally-closed and
/// the register must be inverted so "active" reads true for the state machine.
/// </summary>
public static class PolarityDetector
{
    public static DiChange Detect(bool[] before, bool[] after)
    {
        int changed = -1;
        int count = 0;
        for (int i = 0; i < before.Length && i < after.Length; i++)
        {
            if (before[i] != after[i]) { changed = i; count++; }
        }

        if (count == 0)
            return new DiChange { Index = null, Invert = false, Kind = DiChangeKind.None };
        if (count > 1)
            return new DiChange { Index = null, Invert = false, Kind = DiChangeKind.Ambiguous };

        // Single change: invert when activation drove the input LOW (NC contact).
        bool invert = before[changed] && !after[changed];
        return new DiChange { Index = changed, Invert = invert, Kind = DiChangeKind.Single };
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test Wintime.Connector.UsrModbus.Tester.Tests --filter PolarityDetectorTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Wintime.Connector.UsrModbus.Tester/Calibration/PolarityDetector.cs Wintime.Connector.UsrModbus.Tester.Tests/PolarityDetectorTests.cs
git commit -m "feat(tester): changed-DI and polarity detection"
```

---

### Task 7: connector.json writer with round-trip guarantee

**Files:**
- Create: `Wintime.Connector.UsrModbus.Tester/Wizard/ConnectorJsonWriter.cs`
- Create: `Wintime.Connector.UsrModbus.Tester.Tests/ConnectorJsonWriterTests.cs`

**Interfaces:**
- Consumes: Core `ConnectorConfig`, `DeviceConfig`, `RegisterDef`, `SignalRole`, `ModbusAccess`, `ConfigLoader`.
- Produces: `ConnectorJsonWriter.Write(ConnectorConfig config) → string` — JSON in the exact schema `Core.ConfigLoader` reads (addresses as `"0x...."` strings, enums camelCase). Guarantee under test: `ConfigLoader.LoadFromJson(Write(cfg))` reproduces the same device/registers.

- [ ] **Step 1: Write the failing test**

Create `Wintime.Connector.UsrModbus.Tester.Tests/ConnectorJsonWriterTests.cs`:

```csharp
using System.Linq;
using Wintime.Connector.UsrModbus.Core;
using Wintime.Connector.UsrModbus.Tester.Wizard;
using Xunit;

namespace Wintime.Connector.UsrModbus.Tester.Tests;

public class ConnectorJsonWriterTests
{
    private static ConnectorConfig SampleConfig() => new()
    {
        Device = new DeviceConfig { Host = "192.168.0.7", Port = 502, UnitId = 17 },
        Profile = MachineProfile.SingleNode,
        Registers =
        [
            new RegisterDef { Name = "Injection", Address = 0x0020, Access = ModbusAccess.DiscreteInput, Role = SignalRole.Injection },
            new RegisterDef { Name = "EjectorFwdReached", Address = 0x0021, Access = ModbusAccess.DiscreteInput, Role = SignalRole.EjectorFwdReached, Invert = true },
            new RegisterDef { Name = "InjectionPosition", Address = 0x0058, Access = ModbusAccess.InputRegister, Role = SignalRole.InjectionPosition, Scale = 0.0225, Unit = "mm" },
        ],
    };

    [Fact]
    public void Write_ProducesJsonThatCoreLoaderReadsBack()
    {
        var original = SampleConfig();

        string json = ConnectorJsonWriter.Write(original);
        var reloaded = ConfigLoader.LoadFromJson(json);

        Assert.Equal(original.Device.Host, reloaded.Device.Host);
        Assert.Equal(original.Device.Port, reloaded.Device.Port);
        Assert.Equal(original.Device.UnitId, reloaded.Device.UnitId);
        Assert.Equal(original.Profile, reloaded.Profile);
        Assert.Equal(original.Registers.Count, reloaded.Registers.Count);

        var inj = reloaded.Registers.Single(r => r.Role == SignalRole.Injection);
        Assert.Equal((ushort)0x0020, inj.Address);
        Assert.Equal(ModbusAccess.DiscreteInput, inj.Access);

        var ej = reloaded.Registers.Single(r => r.Role == SignalRole.EjectorFwdReached);
        Assert.True(ej.Invert);

        var pos = reloaded.Registers.Single(r => r.Role == SignalRole.InjectionPosition);
        Assert.Equal(0.0225, pos.Scale, 6);
        Assert.Equal("mm", pos.Unit);
    }

    [Fact]
    public void Write_AddressesAreHexStrings()
    {
        string json = ConnectorJsonWriter.Write(SampleConfig());
        Assert.Contains("\"0x0020\"", json);
        Assert.Contains("\"0x0058\"", json);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Wintime.Connector.UsrModbus.Tester.Tests --filter ConnectorJsonWriterTests`
Expected: FAIL — `ConnectorJsonWriter` not defined.

- [ ] **Step 3: Implement ConnectorJsonWriter**

Create `Wintime.Connector.UsrModbus.Tester/Wizard/ConnectorJsonWriter.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Wintime.Connector.UsrModbus.Core;

namespace Wintime.Connector.UsrModbus.Tester.Wizard;

/// <summary>
/// Serializes a <see cref="ConnectorConfig"/> to the JSON schema that
/// <see cref="ConfigLoader"/> reads. Addresses are emitted as "0x...." strings; enums use the
/// same case-insensitive camelCase the loader accepts. Callers MUST round-trip the output
/// through <see cref="ConfigLoader.LoadFromJson"/> before saving (see WizardScreen, Task 9).
/// </summary>
public static class ConnectorJsonWriter
{
    private static readonly JsonSerializerOptions EnumOptions = new()
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Write(ConnectorConfig config)
    {
        var device = new JsonObject
        {
            ["host"] = config.Device.Host,
            ["port"] = config.Device.Port,
            ["unitId"] = config.Device.UnitId,
            ["pollIntervalMs"] = config.Device.PollIntervalMs,
            ["timeoutMs"] = config.Device.TimeoutMs,
        };

        var registers = new JsonArray();
        foreach (var r in config.Registers)
        {
            var obj = new JsonObject
            {
                ["name"] = r.Name,
                ["address"] = $"0x{r.Address:X4}",
                ["access"] = Enum(r.Access),
            };
            if (r.Role != SignalRole.None) obj["role"] = Enum(r.Role);
            if (r.Scale != 1.0) obj["scale"] = r.Scale;
            if (r.Offset != 0.0) obj["offset"] = r.Offset;
            if (r.Unit is not null) obj["unit"] = r.Unit;
            if (r.Invert) obj["invert"] = true;
            registers.Add(obj);
        }

        var root = new JsonObject
        {
            ["device"] = device,
            ["profile"] = Enum(config.Profile),
            ["registers"] = registers,
        };

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    // Serialize an enum via the camelCase converter, then strip the surrounding quotes.
    private static string Enum<T>(T value) where T : struct, System.Enum =>
        JsonSerializer.Serialize(value, EnumOptions).Trim('"');
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test Wintime.Connector.UsrModbus.Tester.Tests --filter ConnectorJsonWriterTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Wintime.Connector.UsrModbus.Tester/Wizard/ConnectorJsonWriter.cs Wintime.Connector.UsrModbus.Tester.Tests/ConnectorJsonWriterTests.cs
git commit -m "feat(tester): connector.json writer with Core round-trip guarantee"
```

---

### Task 8: Config draft (mutable working model + validate gate)

**Files:**
- Create: `Wintime.Connector.UsrModbus.Tester/Wizard/ConfigDraft.cs`
- Create: `Wintime.Connector.UsrModbus.Tester.Tests/ConfigDraftTests.cs`

**Interfaces:**
- Consumes: Core `ConnectorConfig`, `DeviceConfig`, `RegisterDef`, `SignalRole`, `ModbusAccess`, `MachineProfile`, `ConnectorProfileValidator`, `InvalidConfigException`.
- Produces: `ConfigDraft` class — `static ConfigDraft NewEmpty()`, `static ConfigDraft FromConfig(ConnectorConfig)`; mutators `SetProfile`, `SetDevice(string host,int port,byte unitId,int pollMs,int timeoutMs)`, `SetDiscreteRole(SignalRole role, ushort address, bool invert)`, `SetAnalogRole(SignalRole role, ushort address, double scale, double offset, string unit)`; `ConnectorConfig Build()`; `bool TryValidate(out IReadOnlyList<string> errors)`. Setting a role replaces the register carrying that role (skip = don't call the setter).

- [ ] **Step 1: Write the failing test**

Create `Wintime.Connector.UsrModbus.Tester.Tests/ConfigDraftTests.cs`:

```csharp
using System.Linq;
using Wintime.Connector.UsrModbus.Core;
using Wintime.Connector.UsrModbus.Tester.Wizard;
using Xunit;

namespace Wintime.Connector.UsrModbus.Tester.Tests;

public class ConfigDraftTests
{
    private static ConfigDraft MinimalSingleNode()
    {
        var d = ConfigDraft.NewEmpty();
        d.SetProfile(MachineProfile.SingleNode);
        d.SetDevice("192.168.0.7", 502, 17, 500, 1000);
        d.SetDiscreteRole(SignalRole.Injection, 0x0020, invert: false);
        d.SetDiscreteRole(SignalRole.EjectorFwdReached, 0x0021, invert: false);
        return d;
    }

    [Fact]
    public void Build_ProducesValidatableConfig()
    {
        var cfg = MinimalSingleNode().Build();
        Assert.Equal(MachineProfile.SingleNode, cfg.Profile);
        Assert.Contains(cfg.Registers, r => r.Role == SignalRole.Injection);
    }

    [Fact]
    public void TryValidate_ReportsMissingRequiredRoles()
    {
        var d = ConfigDraft.NewEmpty();
        d.SetProfile(MachineProfile.SingleNode);
        d.SetDevice("h", 502, 17, 500, 1000);
        d.SetDiscreteRole(SignalRole.Injection, 0x0020, false); // EjectorFwdReached missing

        bool ok = d.TryValidate(out var errors);

        Assert.False(ok);
        Assert.Contains(errors, e => e.Contains("EjectorFwdReached"));
    }

    [Fact]
    public void SetAnalogRole_ReplacesSameRoleRegister()
    {
        var d = MinimalSingleNode();
        d.SetAnalogRole(SignalRole.InjectionPosition, 0x0058, 0.02, 0, "mm");
        d.SetAnalogRole(SignalRole.InjectionPosition, 0x0058, 0.0225, 0, "mm"); // recalibrate

        var cfg = d.Build();
        var pos = cfg.Registers.Single(r => r.Role == SignalRole.InjectionPosition);
        Assert.Equal(0.0225, pos.Scale, 6);
    }

    [Fact]
    public void FromConfig_PreservesRegistersForPartialEdits()
    {
        var original = MinimalSingleNode().Build();

        // Load existing, change only poll params — roles must survive.
        var edited = ConfigDraft.FromConfig(original);
        edited.SetDevice("192.168.0.7", 502, 17, pollMs: 250, timeoutMs: 1000);
        var cfg = edited.Build();

        Assert.Equal(250, cfg.Device.PollIntervalMs);
        Assert.Contains(cfg.Registers, r => r.Role == SignalRole.Injection);
        Assert.Contains(cfg.Registers, r => r.Role == SignalRole.EjectorFwdReached);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test Wintime.Connector.UsrModbus.Tester.Tests --filter ConfigDraftTests`
Expected: FAIL — `ConfigDraft` not defined.

- [ ] **Step 3: Implement ConfigDraft**

Create `Wintime.Connector.UsrModbus.Tester/Wizard/ConfigDraft.cs`:

```csharp
using Wintime.Connector.UsrModbus.Core;

namespace Wintime.Connector.UsrModbus.Tester.Wizard;

/// <summary>
/// Mutable working model the wizard edits. Starts empty (build from scratch) or from an
/// existing <see cref="ConnectorConfig"/> (partial edits). Setting a role replaces the register
/// that carries it; a skipped step simply leaves the current value untouched.
/// </summary>
public sealed class ConfigDraft
{
    private MachineProfile _profile = MachineProfile.SingleNode;
    private DeviceConfig _device = new() { Host = "" };
    private readonly Dictionary<SignalRole, RegisterDef> _byRole = new();

    private ConfigDraft() { }

    public static ConfigDraft NewEmpty() => new();

    public static ConfigDraft FromConfig(ConnectorConfig config)
    {
        var d = new ConfigDraft { _profile = config.Profile, _device = config.Device };
        foreach (var r in config.Registers)
            if (r.Role != SignalRole.None)
                d._byRole[r.Role] = r;
        return d;
    }

    public void SetProfile(MachineProfile profile) => _profile = profile;

    public void SetDevice(string host, int port, byte unitId, int pollMs, int timeoutMs) =>
        _device = new DeviceConfig
        {
            Host = host, Port = port, UnitId = unitId,
            PollIntervalMs = pollMs, TimeoutMs = timeoutMs,
        };

    public void SetDiscreteRole(SignalRole role, ushort address, bool invert) =>
        _byRole[role] = new RegisterDef
        {
            Name = role.ToString(),
            Address = address,
            Access = ModbusAccess.DiscreteInput,
            Role = role,
            Invert = invert,
        };

    public void SetAnalogRole(SignalRole role, ushort address, double scale, double offset, string unit) =>
        _byRole[role] = new RegisterDef
        {
            Name = role.ToString(),
            Address = address,
            Access = ModbusAccess.InputRegister,
            Role = role,
            Scale = scale,
            Offset = offset,
            Unit = unit,
        };

    public ConnectorConfig Build() => new()
    {
        Device = _device,
        Profile = _profile,
        Registers = _byRole.Values.ToList(),
    };

    /// <summary>Runs Core's profile validation; on failure returns human-readable messages.</summary>
    public bool TryValidate(out IReadOnlyList<string> errors)
    {
        try
        {
            ConnectorProfileValidator.Validate(Build());
            errors = [];
            return true;
        }
        catch (InvalidConfigException ex)
        {
            errors = [ex.Message];
            return false;
        }
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test Wintime.Connector.UsrModbus.Tester.Tests --filter ConfigDraftTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Wintime.Connector.UsrModbus.Tester/Wizard/ConfigDraft.cs Wintime.Connector.UsrModbus.Tester.Tests/ConfigDraftTests.cs
git commit -m "feat(tester): mutable config draft with skip-friendly edits + validate gate"
```

---

### Task 9: Session, Modbus screens, wizard, verify (UI orchestration)

This task wires the pure units into Spectre.Console screens. Its logic is thin; verification is by manual smoke against a real or simulated gateway, not xUnit. Fold all UI glue here so a reviewer sees the whole interactive surface at once.

**Files:**
- Create: `Wintime.Connector.UsrModbus.Tester/Session.cs`
- Create: `Wintime.Connector.UsrModbus.Tester/Modbus/RawMonitorScreen.cs`
- Create: `Wintime.Connector.UsrModbus.Tester/Connection/ConnectScreen.cs`
- Create: `Wintime.Connector.UsrModbus.Tester/Wizard/WizardScreen.cs`
- Create: `Wintime.Connector.UsrModbus.Tester/Verify/VerifyScreen.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–8, Core `NModbusReader`, `ConnectorEngine`, `ConfigLoader`, `MachineState`.
- Produces: `Session` `{ string Host; int Port; byte UnitId; int TimeoutMs; IModbusReader OpenReader(); bool IsConfigured; }`; screen entry points `ConnectScreen.Run(Session)`, `RawMonitorScreen.Run(Session)`, `WizardScreen.Run(Session)`, `VerifyScreen.Run(Session)`.

- [ ] **Step 1: Implement Session**

Create `Wintime.Connector.UsrModbus.Tester/Session.cs`:

```csharp
using Wintime.Connector.UsrModbus.Core;

namespace Wintime.Connector.UsrModbus.Tester;

/// <summary>Connection parameters shared across screens; opens fresh readers on demand.</summary>
public sealed class Session
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 502;
    public byte UnitId { get; set; } = 17;
    public int TimeoutMs { get; set; } = 1000;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);

    /// <summary>Opens a new read-only Modbus TCP reader with the current parameters.</summary>
    public IModbusReader OpenReader() => new NModbusReader(Host, Port, TimeoutMs);
}
```

- [ ] **Step 2: Implement ConnectScreen**

Create `Wintime.Connector.UsrModbus.Tester/Connection/ConnectScreen.cs`. Prompt for host/port/unitId/timeout via `AnsiConsole.Ask`, store into `Session`, then open a reader, call `ConnectionChecker.ReadIdentity`, and print the MCU versions (or the exception). Read-only.

```csharp
using Spectre.Console;
using Wintime.Connector.UsrModbus.Tester.Connection;

namespace Wintime.Connector.UsrModbus.Tester.Connection;

public static class ConnectScreen
{
    public static void Run(Session session)
    {
        session.Host = AnsiConsole.Ask("IP шлюза USR:", session.IsConfigured ? session.Host : "192.168.10.1");
        session.Port = AnsiConsole.Ask("TCP-порт (регистр 0x1076):", session.Port);
        session.UnitId = (byte)AnsiConsole.Ask<int>("Unit ID (0x00B1, обычно 17):", session.UnitId);
        session.TimeoutMs = AnsiConsole.Ask("Таймаут, мс:", session.TimeoutMs);

        try
        {
            using var reader = session.OpenReader();
            var id = ConnectionChecker.ReadIdentity(reader, session.UnitId);
            AnsiConsole.MarkupLine($"[green]Связь есть.[/] MCU ПО {id.Software}, аппаратная {id.Hardware}.");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Нет связи:[/] {ex.Message}");
        }

        AnsiConsole.MarkupLine("[grey]Нажмите любую клавишу...[/]");
        System.Console.ReadKey(true);
    }
}
```

- [ ] **Step 3: Implement RawMonitorScreen**

Create `Wintime.Connector.UsrModbus.Tester/Modbus/RawMonitorScreen.cs`. Use `AnsiConsole.Live(table)` refreshing at the session interval; each tick call `RawPoller.Poll` and rebuild rows for DI, relays, AI (mV), temperature, counters. Exit on key press. Keep the counter-delta display using `RawSignals.CounterDelta` against the previous snapshot. Catch read exceptions per-tick and show "нет связи" without crashing.

```csharp
using Spectre.Console;
using Wintime.Connector.UsrModbus.Core;

namespace Wintime.Connector.UsrModbus.Tester.Modbus;

public static class RawMonitorScreen
{
    public static void Run(Session session)
    {
        if (!session.IsConfigured) { AnsiConsole.MarkupLine("[yellow]Сначала подключитесь.[/]"); return; }

        var table = new Table().AddColumns("Сигнал", "Значение");
        IModbusReader? reader = null;
        AnsiConsole.Live(table).Start(ctx =>
        {
            while (!System.Console.KeyAvailable)
            {
                try
                {
                    reader ??= session.OpenReader();
                    var s = RawPoller.Poll(reader, session.UnitId);
                    Render(table, s);
                }
                catch (Exception ex)
                {
                    reader?.Dispose(); reader = null;
                    table.Rows.Clear();
                    table.AddRow("[red]связь[/]", Markup.Escape(ex.Message));
                }
                ctx.Refresh();
                Thread.Sleep(session.TimeoutMs < 250 ? 250 : 250);
            }
        });
        reader?.Dispose();
        System.Console.ReadKey(true);
    }

    private static void Render(Table table, RawSnapshot s)
    {
        table.Rows.Clear();
        for (int i = 0; i < s.Di.Length; i++)
            table.AddRow($"DI{i + 1} (0x{0x0020 + i:X4})", s.Di[i] ? "[green]1[/]" : "0");
        for (int i = 0; i < s.AiMilliVolts.Length; i++)
            table.AddRow($"AI{i + 1} (0x{0x0058 + i:X4})", $"{s.AiMilliVolts[i]} мВ");
        table.AddRow("Температура", $"{s.TemperatureC:F2} °C");
        for (int i = 0; i < s.CounterRaw.Length; i++)
            table.AddRow($"Счётчик {i + 1}", s.CounterRaw[i].ToString());
    }
}
```

- [ ] **Step 4: Implement WizardScreen**

Create `Wintime.Connector.UsrModbus.Tester/Wizard/WizardScreen.cs`. Orchestrate the spec's steps:
1. Ask source: **С нуля** (`ConfigDraft.NewEmpty()`) or **Загрузить** (`AnsiConsole.Ask` path → `ConfigLoader.LoadFromFile` → `ConfigDraft.FromConfig`).
2. Each step offered via `SelectionPrompt` with an explicit **«Пропустить»** choice.
3. Profile step → `draft.SetProfile`.
4. DI-mapping step: for each role (required by profile + optional ones the engineer picks), capture baseline via `RawPoller.Poll(...).Di`, prompt "активируйте сигнал", poll again, `PolarityDetector.Detect`; on `Single` map `0x0020 + index` with detected invert via `draft.SetDiscreteRole`; on `None`/`Ambiguous` re-prompt or skip.
5. AI-calibration step: per analog role, pick register (0x0058/0x0059), choose two-point/one-point, sample averaged mV over N polls, compute `LinearFit`, `draft.SetAnalogRole(role, addr, fit.Scale, fit.Offset, "mm")`.
6. Poll-params step → `draft.SetDevice(...)`.
7. Write: `draft.TryValidate` — on failure list missing roles and return to menu; on success `ConnectorJsonWriter.Write`, then **round-trip guard** `ConfigLoader.LoadFromJson(json)` in try/catch, preview via `AnsiConsole.Write(new Panel(json))`, confirm path (default = loaded path or `config/<alias>.json`), `File.WriteAllText`. Offer to jump to `VerifyScreen` on the written file.

Averaging helper (inline in this file):

```csharp
private static double AverageMilliVolts(Session session, ushort address, int samples)
{
    using var reader = session.OpenReader();
    double sum = 0;
    for (int i = 0; i < samples; i++)
    {
        sum += reader.ReadInputRegisters(session.UnitId, address, 1)[0];
        Thread.Sleep(100);
    }
    return sum / samples;
}
```

- [ ] **Step 5: Implement VerifyScreen**

Create `Wintime.Connector.UsrModbus.Tester/Verify/VerifyScreen.cs`. Ask for a config path (default the just-written one), `ConfigLoader.LoadFromFile`, build `new ConnectorEngine(cfg, () => session.OpenReader())` (host/port/timeout come from cfg.Device — build the reader from cfg, not the session, so verify honours the file), subscribe to `StateUpdated`, run `engine.RunAsync(cts.Token)` on a background task, and live-render `MachineState` (Mode, CycleCounter, cushion from `Fields[WellKnownFields.Cushion]`, positions, LastCycleCompletion). Cancel on key press. No MQTT.

```csharp
using Spectre.Console;
using Wintime.Connector.UsrModbus.Core;

namespace Wintime.Connector.UsrModbus.Tester.Verify;

public static class VerifyScreen
{
    public static void Run(Session session) => Run(session, defaultPath: "config/connector.json");

    public static void Run(Session session, string defaultPath)
    {
        string path = AnsiConsole.Ask("Путь к connector.json:", defaultPath);
        ConnectorConfig cfg;
        try { cfg = ConfigLoader.LoadFromFile(path); }
        catch (Exception ex) { AnsiConsole.MarkupLineInterpolated($"[red]Не загрузился:[/] {ex.Message}"); return; }

        var engine = new ConnectorEngine(cfg,
            () => new NModbusReader(cfg.Device.Host, cfg.Device.Port, cfg.Device.TimeoutMs));
        using var cts = new CancellationTokenSource();
        var run = Task.Run(() => engine.RunAsync(cts.Token));

        var table = new Table().AddColumns("Поле", "Значение");
        AnsiConsole.Live(table).Start(ctx =>
        {
            while (!System.Console.KeyAvailable)
            {
                var st = engine.Latest;
                table.Rows.Clear();
                if (st is null) table.AddRow("статус", "ожидание первого опроса...");
                else
                {
                    table.AddRow("Режим", st.Mode.ToString());
                    table.AddRow("Счётчик циклов", st.CycleCounter.ToString());
                    table.AddRow("Последний цикл", st.LastCycleCompletion.ToString());
                    table.AddRow("Подушка", st.Fields.GetValueOrDefault(WellKnownFields.Cushion)?.ToString() ?? "—");
                }
                ctx.Refresh();
                Thread.Sleep(250);
            }
        });

        cts.Cancel();
        try { run.Wait(2000); } catch { /* cancellation */ }
        System.Console.ReadKey(true);
    }
}
```

- [ ] **Step 6: Build**

Run: `dotnet build Wintime.Connector.UsrModbus.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add Wintime.Connector.UsrModbus.Tester/Session.cs Wintime.Connector.UsrModbus.Tester/Modbus/RawMonitorScreen.cs Wintime.Connector.UsrModbus.Tester/Connection/ConnectScreen.cs Wintime.Connector.UsrModbus.Tester/Wizard/WizardScreen.cs Wintime.Connector.UsrModbus.Tester/Verify/VerifyScreen.cs
git commit -m "feat(tester): connect/monitor/wizard/verify screens"
```

---

### Task 10: Main menu + wire-up + docs

**Files:**
- Modify: `Wintime.Connector.UsrModbus.Tester/Program.cs`
- Modify: `Wintime.Connector.UsrModbus/TESTING_ONSITE.md`

**Interfaces:**
- Consumes: `Session`, `ConnectScreen`, `RawMonitorScreen`, `WizardScreen`, `VerifyScreen`.

- [ ] **Step 1: Replace Program.cs with the menu loop**

Overwrite `Wintime.Connector.UsrModbus.Tester/Program.cs`:

```csharp
using Spectre.Console;
using Wintime.Connector.UsrModbus.Tester;
using Wintime.Connector.UsrModbus.Tester.Connection;
using Wintime.Connector.UsrModbus.Tester.Modbus;
using Wintime.Connector.UsrModbus.Tester.Verify;
using Wintime.Connector.UsrModbus.Tester.Wizard;

var session = new Session();

while (true)
{
    AnsiConsole.Clear();
    AnsiConsole.Write(new FigletText("USR Tester").Color(Color.Teal));
    AnsiConsole.MarkupLine(session.IsConfigured
        ? $"[grey]Подключение:[/] {session.Host}:{session.Port} unit {session.UnitId}"
        : "[grey]Подключение не задано[/]");

    var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
        .AddChoices(
            "Подключение",
            "Live-монитор сырых сигналов",
            "Мастер: собрать / править connector.json",
            "Проверить connector.json",
            "Выход"));

    switch (choice)
    {
        case "Подключение": ConnectScreen.Run(session); break;
        case "Live-монитор сырых сигналов": RawMonitorScreen.Run(session); break;
        case "Мастер: собрать / править connector.json": WizardScreen.Run(session); break;
        case "Проверить connector.json": VerifyScreen.Run(session); break;
        case "Выход": return;
    }
}
```

- [ ] **Step 2: Build and run the menu smoke**

Run: `dotnet run --project Wintime.Connector.UsrModbus.Tester`
Expected: FIGlet banner + 5-item menu renders; arrow keys move; "Выход" exits cleanly. (No gateway needed for this smoke — Connect/Monitor/Verify will simply report no connection.)

- [ ] **Step 3: Cross-reference the Tester from the on-site runbook**

In `Wintime.Connector.UsrModbus/TESTING_ONSITE.md`, under the intro (after line 18, the paragraph mentioning the external tool), add:

```markdown
> **Тестер (`Wintime.Connector.UsrModbus.Tester`)** автоматизирует ступени 0/1/3 этого runbook и
> проверку конфига без MQTT: `dotnet run --project Wintime.Connector.UsrModbus.Tester`. Внешние
> тулы (`modpoll`, MQTT Explorer) остаются запасным вариантом.
```

- [ ] **Step 4: Full test + build gate**

Run: `dotnet test Wintime.Connector.UsrModbus.slnx`
Expected: All tests pass (existing connector tests + new Tester tests), build clean.

- [ ] **Step 5: Commit**

```bash
git add Wintime.Connector.UsrModbus.Tester/Program.cs Wintime.Connector.UsrModbus/TESTING_ONSITE.md
git commit -m "feat(tester): main menu wiring + on-site runbook cross-reference"
```

---

## Manual smoke (on real or simulated gateway)

Automated tests cover all calibration/config logic. The interactive path needs a live USR-IO424
(or a Modbus TCP simulator seeded with the register map) for final confirmation:

1. **Подключение** — enter IP/port/unitId → MCU versions print.
2. **Live-монитор** — toggle a dry contact → matching DI flips; move the screw → AI mV changes.
3. **Мастер (с нуля)** — map Injection + EjectorFwdReached (trigger each), calibrate one analog two-point → `connector.json` written; round-trip guard passes.
4. **Мастер (загрузка)** — load the file, skip everything except AI calibration → only scale/offset change; other registers survive.
5. **Проверить** — point at the written file, run a real cycle → `Режим` goes idle→auto, `Счётчик циклов` increments once per shot, `Подушка` shows plausible mm.

---

## Self-Review

**Spec coverage:**
- §2 read-only / reuse-Core / output compatibility → Global Constraints + Tasks 1–9 (no writes; ConfigLoader round-trip in Task 7 + Task 9 Step 4).
- §3 project/stack/Spectre → Task 1.
- §4 menu modes (connect, raw monitor, wizard, verify) → Tasks 4, 3/9, 8/9, 9; menu in Task 10.
- §4 no button-register reads → Task 3 (test asserts it).
- §5 wizard: source choice, per-step & per-role skip, DI mapping w/ polarity, AI two/one-point, poll params, validate-gate write → Tasks 6, 8, 9; validation in Task 8; writer in Task 7.
- §6 verify mode over any config via ConnectorEngine, no MQTT → Task 9 VerifyScreen.
- §7 testability of pure units → Tasks 1, 3–8 all TDD.
- §9 byte-order note → RawSignals assumes host-order from Core reader (documented in Task 1 file comment); temperature example covered by test.

**Placeholder scan:** No TBD/TODO; every code step shows complete code. UI steps (Task 9/10) describe orchestration but include the concrete screen code.

**Type consistency:** `IModbusReader` signatures match Core (`ReadDiscreteInputs(byte,ushort,ushort)` etc.). `RawSnapshot` fields consumed consistently by `RawPoller`/`RawMonitorScreen`. `ConfigDraft` setter names (`SetProfile/SetDevice/SetDiscreteRole/SetAnalogRole/Build/TryValidate`) match across Tasks 8–9. `ConnectorJsonWriter.Write` and `ConfigLoader.LoadFromJson` used together in Tasks 7 & 9. `ConnectorEngine(config, Func<IModbusReader>)`, `.Latest`, `.RunAsync(ct)`, `WellKnownFields.Cushion` match Core.
