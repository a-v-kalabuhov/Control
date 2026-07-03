namespace UsrIoPoller;

/// <summary>Таблица данных Modbus / способ доступа. Определяет функциональный код чтения.</summary>
public enum ModbusAccess
{
    /// <summary>Coil, чтение через FC 0x01 (реле — обратное чтение).</summary>
    Coil,
    /// <summary>Discrete input, чтение через FC 0x02 (дискретный вход, уровень).</summary>
    DiscreteInput,
    /// <summary>Holding register, чтение через FC 0x03.</summary>
    HoldingRegister,
    /// <summary>Input register, чтение через FC 0x04.</summary>
    InputRegister,
}

/// <summary>Как интерпретировать сырые данные регистра до применения формулы/маппинга.</summary>
public enum RawType
{
    /// <summary>Один бит (для Coil/DiscreteInput): 0 или 1.</summary>
    Bit,
    /// <summary>16-битное беззнаковое слово.</summary>
    UInt16,
    /// <summary>16-битное знаковое слово.</summary>
    Int16,
}

/// <summary>Тип целевого значения тега после преобразования.</summary>
public enum TargetKind
{
    Bool,
    Int,
    String,
    Double,
}

/// <summary>Параметры подключения и опроса устройства.</summary>
public sealed record DeviceConfig
{
    /// <summary>IP-адрес USR-IO424T-EWR.</summary>
    public required string Host { get; init; }

    /// <summary>TCP-порт. Стандартный Modbus TCP = 502; у USR порт задаётся регистром 0x1076 — уточните.</summary>
    public int Port { get; init; } = 502;

    /// <summary>Modbus-адрес устройства (Unit ID). Заводское значение обычно 17.</summary>
    public byte UnitId { get; init; } = 17;

    /// <summary>Период опроса в миллисекундах. Для задачи с ТПА достаточно 500.</summary>
    public int PollIntervalMs { get; init; } = 500;

    /// <summary>Таймаут чтения/записи в миллисекундах.</summary>
    public int TimeoutMs { get; init; } = 1000;

    /// <summary>
    /// Максимальный «зазор» между соседними регистрами, при котором они всё ещё
    /// объединяются в один запрос. 0 — только строго смежные адреса.
    /// </summary>
    public ushort MaxBatchGap { get; init; } = 0;
}

/// <summary>
/// Описание одного считываемого тега (регистра). Задаёт только stateless-преобразование
/// «сырое значение → мгновенное значение тега». Никакой логики с состоянием здесь нет.
/// </summary>
public sealed record RegisterDef
{
    /// <summary>Уникальное имя тега.</summary>
    public required string Name { get; init; }

    /// <summary>Адрес регистра.</summary>
    public required ushort Address { get; init; }

    /// <summary>Способ доступа (определяет функциональный код).</summary>
    public required ModbusAccess Access { get; init; }

    /// <summary>Интерпретация сырых данных.</summary>
    public RawType RawType { get; init; } = RawType.UInt16;

    /// <summary>Число регистров/бит на тег (для 32-битных значений и т.п.). Обычно 1.</summary>
    public ushort Count { get; init; } = 1;

    // --- Ветка непрерывных величин (аналог): физ = raw * Scale + Offset ---

    /// <summary>Множитель линейного пересчёта (например, 0.001 для мВ → В).</summary>
    public double Scale { get; init; } = 1.0;

    /// <summary>Смещение линейного пересчёта.</summary>
    public double Offset { get; init; } = 0.0;

    /// <summary>Единица измерения целевого значения (например, "V").</summary>
    public string? Unit { get; init; }

    // --- Ветка дискретных величин: raw → целевое значение по таблице ---

    /// <summary>Таблица соответствия «сырое числовое значение → целевое значение».
    /// Если задана — применяется она, а Scale/Offset игнорируются.</summary>
    public IReadOnlyDictionary<long, object?>? ValueMap { get; init; }

    /// <summary>Тип целевого значения.</summary>
    public TargetKind Target { get; init; } = TargetKind.Double;

    /// <summary>Доступ битовый (Coil/DiscreteInput)?</summary>
    public bool IsBitAccess => Access is ModbusAccess.Coil or ModbusAccess.DiscreteInput;

    /// <summary>Тег обрабатывается через таблицу соответствия (дискретный), а не формулой?</summary>
    public bool IsMapped => ValueMap is not null;
}

/// <summary>Полная конфигурация: устройство + список тегов.</summary>
public sealed record PollingConfig
{
    public required DeviceConfig Device { get; init; }
    public required IReadOnlyList<RegisterDef> Registers { get; init; }
}
