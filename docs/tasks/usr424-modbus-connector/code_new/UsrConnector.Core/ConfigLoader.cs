using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UsrConnector.Core;

/// <summary>Загрузка и валидация <see cref="ConnectorConfig"/> из JSON.</summary>
public static class ConfigLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // Без naming policy: enum-значения сопоставляются без учёта регистра
        // ("discreteInput", "uint16", "injection", "singleNode" распарсятся корректно).
        Converters = { new JsonStringEnumConverter() },
    };

    public static ConnectorConfig LoadFromFile(string path) => LoadFromJson(File.ReadAllText(path));

    public static ConnectorConfig LoadFromJson(string json)
    {
        var dto = JsonSerializer.Deserialize<RootDto>(json, Options)
                  ?? throw new InvalidConfigException("Пустая конфигурация.");

        if (dto.Device is null) throw new InvalidConfigException("Секция 'device' обязательна.");
        if (dto.Registers is null || dto.Registers.Count == 0)
            throw new InvalidConfigException("Список 'registers' пуст.");

        var config = new ConnectorConfig
        {
            Device = new DeviceConfig
            {
                Host = Require(dto.Device.Host, "device.host"),
                Port = dto.Device.Port ?? 502,
                UnitId = dto.Device.UnitId ?? 17,
                PollIntervalMs = dto.Device.PollIntervalMs ?? 500,
                TimeoutMs = dto.Device.TimeoutMs ?? 1000,
                MaxBatchGap = dto.Device.MaxBatchGap ?? 0,
            },
            Registers = dto.Registers.Select(BuildRegister).ToList(),
            Profile = dto.Profile ?? MachineProfile.SingleNode,
            StateMachine = BuildStateMachine(dto.StateMachine),
        };

        ConnectorProfileValidator.Validate(config);
        return config;
    }

    private static RegisterDef BuildRegister(RegisterDto r)
    {
        var name = Require(r.Name, "register.name");
        var access = r.Access ?? throw new InvalidConfigException($"[{name}] 'access' обязателен.");
        var role = r.Role ?? SignalRole.None;

        var rawType = r.RawType ?? (access is ModbusAccess.Coil or ModbusAccess.DiscreteInput
            ? RawType.Bit : RawType.UInt16);

        return new RegisterDef
        {
            Name = name,
            Address = ParseAddress(r.Address, name),
            Access = access,
            RawType = rawType,
            Count = r.Count ?? 1,
            Role = role,
            Scale = r.Scale ?? 1.0,
            Offset = r.Offset ?? 0.0,
            Unit = r.Unit,
            Invert = r.Invert ?? false,
        };
    }

    private static StateMachineSettings BuildStateMachine(StateMachineDto? s) => s is null
        ? new StateMachineSettings()
        : new StateMachineSettings
        {
            SeedCycleMs = s.SeedCycleMs ?? 300_000,
            AlarmTimeoutCoef = s.AlarmTimeoutCoef ?? 2.0,
            IdleTimeoutCoef = s.IdleTimeoutCoef ?? 3.0,
            AverageWindowCycles = s.AverageWindowCycles ?? 10,
            StatisticsResetAfterMs = s.StatisticsResetAfterMs ?? 900_000,
            OfflineAfterFailedPolls = s.OfflineAfterFailedPolls ?? 3,
        };

    private static ushort ParseAddress(string? raw, string name)
    {
        var s = Require(raw, $"[{name}] address").Trim();
        long value = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.Parse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : long.Parse(s, CultureInfo.InvariantCulture);

        if (value is < 0 or > ushort.MaxValue)
            throw new InvalidConfigException($"[{name}] адрес вне диапазона ushort: {s}.");
        return (ushort)value;
    }

    private static string Require(string? value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidConfigException($"Поле '{field}' обязательно.")
            : value;

    // --- DTO (nullable => отличаем «не задано» от дефолта) ---

    private sealed record RootDto
    {
        public DeviceDto? Device { get; init; }
        public List<RegisterDto>? Registers { get; init; }
        public MachineProfile? Profile { get; init; }
        public StateMachineDto? StateMachine { get; init; }
    }

    private sealed record DeviceDto
    {
        public string? Host { get; init; }
        public int? Port { get; init; }
        public byte? UnitId { get; init; }
        public int? PollIntervalMs { get; init; }
        public int? TimeoutMs { get; init; }
        public ushort? MaxBatchGap { get; init; }
    }

    private sealed record RegisterDto
    {
        public string? Name { get; init; }
        public string? Address { get; init; }
        public ModbusAccess? Access { get; init; }
        public RawType? RawType { get; init; }
        public ushort? Count { get; init; }
        public SignalRole? Role { get; init; }
        public double? Scale { get; init; }
        public double? Offset { get; init; }
        public string? Unit { get; init; }
        public bool? Invert { get; init; }
    }

    private sealed record StateMachineDto
    {
        public double? SeedCycleMs { get; init; }
        public double? AlarmTimeoutCoef { get; init; }
        public double? IdleTimeoutCoef { get; init; }
        public int? AverageWindowCycles { get; init; }
        public double? StatisticsResetAfterMs { get; init; }
        public int? OfflineAfterFailedPolls { get; init; }
    }
}
