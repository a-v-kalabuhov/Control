namespace UsrConnector.Core;

/// <summary>Таблица данных Modbus / способ доступа. Определяет функциональный код чтения.</summary>
public enum ModbusAccess
{
    /// <summary>Coil, FC 0x01 (реле — обратное чтение).</summary>
    Coil,
    /// <summary>Discrete input, FC 0x02 (дискретный вход).</summary>
    DiscreteInput,
    /// <summary>Holding register, FC 0x03.</summary>
    HoldingRegister,
    /// <summary>Input register, FC 0x04.</summary>
    InputRegister,
}

/// <summary>Интерпретация сырых данных регистра.</summary>
public enum RawType
{
    Bit,
    UInt16,
    Int16,
}

/// <summary>Профиль машины: набор обязательных ролей (см. <see cref="ConnectorProfileValidator"/>).</summary>
public enum MachineProfile
{
    /// <summary>Одноузловая машина (пилот): Injection + EjectorFwdReached обязательны.</summary>
    SingleNode,
    /// <summary>Двухузловая машина (2K): дополнительно InjectionPosition2 (вторая подушка).</summary>
    TwoNode,
}

/// <summary>Параметры подключения и опроса устройства.</summary>
public sealed record DeviceConfig
{
    public required string Host { get; init; }

    /// <summary>TCP-порт. У USR задаётся регистром 0x1076 — сверить с фактическим.</summary>
    public int Port { get; init; } = 502;

    /// <summary>Modbus-адрес устройства (Unit ID). Заводское значение USR обычно 17.</summary>
    public byte UnitId { get; init; } = 17;

    public int PollIntervalMs { get; init; } = 500;
    public int TimeoutMs { get; init; } = 1000;

    /// <summary>Макс. зазор адресов при склейке смежных регистров в один запрос.</summary>
    public ushort MaxBatchGap { get; init; } = 0;
}

/// <summary>
/// Описание одного считываемого регистра. Задаёт stateless-преобразование
/// «сырое значение → мгновенное значение» и семантическую роль сигнала.
/// </summary>
public sealed record RegisterDef
{
    /// <summary>Уникальное имя. Для сигналов без роли используется как имя поля в MachineState.Fields.</summary>
    public required string Name { get; init; }

    public required ushort Address { get; init; }
    public required ModbusAccess Access { get; init; }
    public RawType RawType { get; init; } = RawType.UInt16;
    public ushort Count { get; init; } = 1;

    /// <summary>
    /// Семантическая роль сигнала. Прикладная логика (автомат) видит сигнал только через
    /// роль; None = сигнал публикуется как опциональное поле, в логике не участвует.
    /// </summary>
    public SignalRole Role { get; init; } = SignalRole.None;

    // Непрерывные величины: физ = raw * Scale + Offset.
    public double Scale { get; init; } = 1.0;
    public double Offset { get; init; } = 0.0;
    public string? Unit { get; init; }

    /// <summary>Инверсия дискретного сигнала (для NC-контактов): true = замкнуто читается как false.</summary>
    public bool Invert { get; init; }

    public bool IsBitAccess => Access is ModbusAccess.Coil or ModbusAccess.DiscreteInput;
}

/// <summary>Полная конфигурация коннектора (нижний слой).</summary>
public sealed record ConnectorConfig
{
    public required DeviceConfig Device { get; init; }
    public required IReadOnlyList<RegisterDef> Registers { get; init; }
    public MachineProfile Profile { get; init; } = MachineProfile.SingleNode;
    public StateMachineSettings StateMachine { get; init; } = new();
}

/// <summary>Валидация профиля: проверка, что обязательные роли назначены.</summary>
public static class ConnectorProfileValidator
{
    /// <summary>Роли, без которых автомат состояния неработоспособен (см. STATE_MACHINE.md §4).</summary>
    public static IReadOnlyList<SignalRole> RequiredRoles(MachineProfile profile) => profile switch
    {
        MachineProfile.SingleNode =>
            [SignalRole.Injection, SignalRole.EjectorFwdReached],
        MachineProfile.TwoNode =>
            [SignalRole.Injection, SignalRole.EjectorFwdReached, SignalRole.InjectionPosition2],
        _ => throw new ArgumentOutOfRangeException(nameof(profile)),
    };

    public static void Validate(ConnectorConfig config)
    {
        var assigned = config.Registers.Select(r => r.Role).ToHashSet();

        var missing = RequiredRoles(config.Profile).Where(r => !assigned.Contains(r)).ToList();
        if (missing.Count > 0)
            throw new InvalidConfigException(
                $"Профиль {config.Profile}: не назначены обязательные роли: {string.Join(", ", missing)}.");

        // Каждая роль (кроме None) — не более одного регистра.
        var duplicates = config.Registers
            .Where(r => r.Role != SignalRole.None)
            .GroupBy(r => r.Role)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicates.Count > 0)
            throw new InvalidConfigException(
                $"Роль назначена нескольким регистрам: {string.Join(", ", duplicates)}.");

        // Уникальность имён.
        var dupNames = config.Registers.GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupNames.Count > 0)
            throw new InvalidConfigException($"Дублирующиеся имена: {string.Join(", ", dupNames)}.");
    }
}

/// <summary>Ошибка конфигурации.</summary>
public sealed class InvalidConfigException(string message) : Exception(message);
