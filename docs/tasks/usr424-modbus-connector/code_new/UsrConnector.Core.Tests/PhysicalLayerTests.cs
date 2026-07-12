using Xunit;

namespace UsrConnector.Core.Tests;

public class RoleMapperTests
{
    private static readonly DateTimeOffset Ts = MachineSim.T0;

    private static RegisterDef Discrete(string name, SignalRole role, bool invert = false) => new()
    {
        Name = name, Address = 0x20, Access = ModbusAccess.DiscreteInput,
        RawType = RawType.Bit, Role = role, Invert = invert,
    };

    private static RegisterDef Analog(string name, SignalRole role, double scale = 1.0) => new()
    {
        Name = name, Address = 0x58, Access = ModbusAccess.InputRegister, Role = role, Scale = scale,
    };

    [Fact]
    public void DiscreteRoles_MappedToSnapshotFields()
    {
        var samples = new List<RegisterSample>
        {
            RegisterDecoder.FromBit(Discrete("inj", SignalRole.Injection), true),
            RegisterDecoder.FromBit(Discrete("e1", SignalRole.EjectorFwdReached), false),
        };

        var snap = RoleMapper.Map(samples, Ts);

        Assert.True(snap.ConnectionOk);
        Assert.True(snap.Injection);
        Assert.False(snap.EjectorFwdReached);
    }

    [Fact]
    public void AnalogRole_ScaledValue_MappedToSnapshot()
    {
        var def = Analog("pos", SignalRole.InjectionPosition, scale: 0.0225); // мВ -> мм
        var samples = new List<RegisterSample>
        {
            RegisterDecoder.FromWords(def, stackalloc ushort[] { 1000 }), // 1000 мВ
        };

        var snap = RoleMapper.Map(samples, Ts);

        Assert.Equal(22.5, snap.InjectionPosition!.Value, precision: 3);
    }

    [Fact]
    public void NoneRole_GoesToExtraFields_ByName()
    {
        // Принцип прозрачности: сигнал без роли публикуется как есть под своим именем.
        var samples = new List<RegisterSample>
        {
            RegisterDecoder.FromWords(Analog("chillerTemp", SignalRole.None), stackalloc ushort[] { 12 }),
        };

        var snap = RoleMapper.Map(samples, Ts);

        Assert.Equal(12.0, snap.ExtraFields!["chillerTemp"]);
    }

    [Fact]
    public void InvertedDiscrete_FlipsValue()
    {
        // NC-контакт: физически замкнут = логически false.
        var samples = new List<RegisterSample>
        {
            RegisterDecoder.FromBit(Discrete("inj", SignalRole.Injection, invert: true), true),
        };

        Assert.False(RoleMapper.Map(samples, Ts).Injection);
    }
}

public class ProfileValidationTests
{
    private static ConnectorConfig Config(MachineProfile profile, params SignalRole[] roles) => new()
    {
        Device = new DeviceConfig { Host = "localhost" },
        Profile = profile,
        Registers = roles.Select((r, i) => new RegisterDef
        {
            Name = $"reg{i}", Address = (ushort)(0x20 + i),
            Access = ModbusAccess.DiscreteInput, Role = r,
        }).ToList(),
    };

    [Fact]
    public void SingleNode_RequiresInjectionAndEjectorFwd()
    {
        // Без якоря цикла и штатного завершения автомат неработоспособен (STATE_MACHINE.md §4).
        var bad = Config(MachineProfile.SingleNode, SignalRole.Injection); // нет EjectorFwdReached
        Assert.Throws<InvalidConfigException>(() => ConnectorProfileValidator.Validate(bad));

        var ok = Config(MachineProfile.SingleNode, SignalRole.Injection, SignalRole.EjectorFwdReached);
        ConnectorProfileValidator.Validate(ok); // не бросает
    }

    [Fact]
    public void TwoNode_AdditionallyRequiresInjectionPosition2()
    {
        var bad = Config(MachineProfile.TwoNode, SignalRole.Injection, SignalRole.EjectorFwdReached);
        Assert.Throws<InvalidConfigException>(() => ConnectorProfileValidator.Validate(bad));
    }

    [Fact]
    public void DuplicateRole_Rejected()
    {
        var bad = Config(MachineProfile.SingleNode,
            SignalRole.Injection, SignalRole.EjectorFwdReached, SignalRole.Injection);
        Assert.Throws<InvalidConfigException>(() => ConnectorProfileValidator.Validate(bad));
    }
}

public class ConfigLoaderTests
{
    [Fact]
    public void LoadsPilotStyleConfig_WithRolesAndStateMachineSettings()
    {
        const string json = """
        {
          "device": { "host": "192.168.0.7", "unitId": 17 },
          "profile": "singleNode",
          "stateMachine": { "seedCycleMs": 60000, "alarmTimeoutCoef": 2.5 },
          "registers": [
            { "name": "Injection", "address": "0x0020", "access": "discreteInput", "role": "injection" },
            { "name": "E1", "address": "0x0021", "access": "discreteInput", "role": "ejectorFwdReached" },
            { "name": "InjPos", "address": "0x0058", "access": "inputRegister",
              "role": "injectionPosition", "scale": 0.0225, "unit": "mm" }
          ]
        }
        """;

        var config = ConfigLoader.LoadFromJson(json);

        Assert.Equal(MachineProfile.SingleNode, config.Profile);
        Assert.Equal(60_000, config.StateMachine.SeedCycleMs);
        Assert.Equal(2.5, config.StateMachine.AlarmTimeoutCoef);
        Assert.Equal(SignalRole.Injection, config.Registers[0].Role);
        Assert.Equal(0x0020, config.Registers[0].Address);
        Assert.Equal(0.0225, config.Registers[2].Scale);
    }

    [Fact]
    public void MissingRequiredRole_FailsAtLoad()
    {
        const string json = """
        {
          "device": { "host": "h" },
          "registers": [
            { "name": "Injection", "address": "0x0020", "access": "discreteInput", "role": "injection" }
          ]
        }
        """;

        Assert.Throws<InvalidConfigException>(() => ConfigLoader.LoadFromJson(json));
    }
}

public class BatchPlannerTests
{
    private static RegisterDef At(ushort addr, ModbusAccess access) => new()
    {
        Name = $"r{addr:X}", Address = addr, Access = access,
    };

    [Fact]
    public void ContiguousSameAccess_MergedIntoSingleBlock()
    {
        var regs = new[]
        {
            At(0x20, ModbusAccess.DiscreteInput), At(0x21, ModbusAccess.DiscreteInput),
            At(0x22, ModbusAccess.DiscreteInput), At(0x23, ModbusAccess.DiscreteInput),
        };

        var blocks = BatchPlanner.Plan(regs);

        var b = Assert.Single(blocks);
        Assert.Equal(0x20, b.Start);
        Assert.Equal(4, b.Quantity);
    }

    [Fact]
    public void DifferentAccess_SeparateBlocks()
    {
        var regs = new[]
        {
            At(0x20, ModbusAccess.DiscreteInput),
            At(0x58, ModbusAccess.InputRegister), At(0x59, ModbusAccess.InputRegister),
        };

        var blocks = BatchPlanner.Plan(regs);

        Assert.Equal(2, blocks.Count);
    }
}

public class TwoNodeCushionTests
{
    [Fact]
    public void SecondCushion_TrackedOverSameCycleWindow_AndPublished()
    {
        // 2K: обе подушки — минимумы за одно окно цикла (узлы работают синхронно).
        var fsm = new MachineStateMachine(MachineSim.DefaultSettings, MachineSim.T0);
        var t = MachineSim.T0;
        MachineState S(bool inj, bool e1, double p1, double p2)
        {
            var st = fsm.Process(new RoleSnapshot
            {
                TimestampUtc = t, ConnectionOk = true,
                Injection = inj, EjectorFwdReached = e1,
                InjectionPosition = p1, InjectionPosition2 = p2,
            });
            t += TimeSpan.FromMilliseconds(500);
            return st;
        }

        S(true, false, 50, 60);
        S(true, false, 5, 8);    // минимумы обоих узлов
        S(false, false, 30, 40); // набор дозы
        var final = S(false, true, 50, 60); // E1

        Assert.Equal(5.0, Assert.IsType<double>(final.Fields[WellKnownFields.Cushion]));
        Assert.Equal(8.0, Assert.IsType<double>(final.Fields[WellKnownFields.Cushion2]));
    }
}
