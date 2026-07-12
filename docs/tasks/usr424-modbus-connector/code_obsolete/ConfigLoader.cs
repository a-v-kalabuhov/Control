using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UsrIoPoller;

/// <summary>Загружает и валидирует <see cref="PollingConfig"/> из JSON.</summary>
public static class ConfigLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // Без naming policy: имена enum сопоставляются без учёта регистра
        // ("discreteInput", "uint16", "inputRegister" и т.п. распарсятся корректно).
        Converters = { new JsonStringEnumConverter() },
    };

    public static PollingConfig LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        return LoadFromJson(json);
    }

    public static PollingConfig LoadFromJson(string json)
    {
        var dto = JsonSerializer.Deserialize<RootDto>(json, Options)
                  ?? throw new InvalidConfigException("Пустая конфигурация.");

        if (dto.Device is null)
            throw new InvalidConfigException("Секция 'device' обязательна.");
        if (dto.Registers is null || dto.Registers.Count == 0)
            throw new InvalidConfigException("Список 'registers' пуст.");

        var device = new DeviceConfig
        {
            Host = Require(dto.Device.Host, "device.host"),
            Port = dto.Device.Port ?? 502,
            UnitId = dto.Device.UnitId ?? 17,
            PollIntervalMs = dto.Device.PollIntervalMs ?? 500,
            TimeoutMs = dto.Device.TimeoutMs ?? 1000,
            MaxBatchGap = dto.Device.MaxBatchGap ?? 0,
        };

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var registers = new List<RegisterDef>(dto.Registers.Count);

        foreach (var r in dto.Registers)
            registers.Add(BuildRegister(r, seenNames));

        return new PollingConfig { Device = device, Registers = registers };
    }

    private static RegisterDef BuildRegister(RegisterDto r, HashSet<string> seenNames)
    {
        var name = Require(r.Name, "register.name");
        if (!seenNames.Add(name))
            throw new InvalidConfigException($"Дублирующееся имя тега: '{name}'.");

        var access = r.Access ?? throw new InvalidConfigException($"[{name}] 'access' обязателен.");
        var address = ParseAddress(r.Address, name);
        var rawType = r.RawType ?? (access is ModbusAccess.Coil or ModbusAccess.DiscreteInput
            ? RawType.Bit
            : RawType.UInt16);
        var count = r.Count ?? 1;
        if (count < 1)
            throw new InvalidConfigException($"[{name}] 'count' должен быть >= 1.");

        var target = r.Target ?? TargetKind.Double;
        IReadOnlyDictionary<long, object?>? valueMap = null;

        if (r.ValueMap is { Count: > 0 })
        {
            // Дискретная ветка: строим таблицу «сырое числовое → целевое значение».
            if (target == TargetKind.Double)
                target = TargetKind.Bool; // разумный дефолт, если тип не указан явно
            valueMap = BuildValueMap(r.ValueMap, target, name);
        }
        else if (target != TargetKind.Double)
        {
            // Немаппированный тег без формулы, но с нечисловым target — почти наверняка ошибка.
            throw new InvalidConfigException(
                $"[{name}] задан target={target}, но нет 'valueMap'. " +
                "Для непрерывных величин используйте target=double со scale/offset.");
        }

        return new RegisterDef
        {
            Name = name,
            Address = address,
            Access = access,
            RawType = rawType,
            Count = count,
            Scale = r.Scale ?? 1.0,
            Offset = r.Offset ?? 0.0,
            Unit = r.Unit,
            Target = target,
            ValueMap = valueMap,
        };
    }

    private static IReadOnlyDictionary<long, object?> BuildValueMap(
        Dictionary<string, JsonElement> raw, TargetKind target, string name)
    {
        var map = new Dictionary<long, object?>();
        foreach (var (key, elem) in raw)
        {
            long numericKey = ParseLongKey(key, name);
            object? value = target switch
            {
                TargetKind.Bool => elem.GetBoolean(),
                TargetKind.Int => elem.GetInt32(),
                TargetKind.String => elem.GetString(),
                TargetKind.Double => elem.GetDouble(),
                _ => throw new InvalidConfigException($"[{name}] неизвестный target."),
            };
            map[numericKey] = value;
        }
        return map;
    }

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

    private static long ParseLongKey(string s, string name)
    {
        s = s.Trim();
        return s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.Parse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : long.Parse(s, CultureInfo.InvariantCulture);
    }

    private static string Require(string? value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidConfigException($"Поле '{field}' обязательно.")
            : value;

    // --- DTO для десериализации (nullable поля => можем отличить «нет значения» от дефолта) ---

    private sealed record RootDto
    {
        public DeviceDto? Device { get; init; }
        public List<RegisterDto>? Registers { get; init; }
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
        public double? Scale { get; init; }
        public double? Offset { get; init; }
        public string? Unit { get; init; }
        public TargetKind? Target { get; init; }
        public Dictionary<string, JsonElement>? ValueMap { get; init; }
    }
}

/// <summary>Ошибка конфигурации.</summary>
public sealed class InvalidConfigException(string message) : Exception(message);
