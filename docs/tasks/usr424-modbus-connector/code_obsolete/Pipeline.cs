namespace UsrIoPoller;

/// <summary>Мгновенное значение тега (результат stateless-декодирования одного опроса).</summary>
public sealed record TagSample
{
    public required string Name { get; init; }
    public required DateTime TimestampUtc { get; init; }

    /// <summary>Сырое числовое значение до формулы/маппинга (для бита — 0/1).</summary>
    public required long RawNumeric { get; init; }

    /// <summary>Целевое значение: bool / int / string / double (или null, если нет соответствия в valueMap).</summary>
    public object? Value { get; init; }

    /// <summary>Физическая величина для непрерывных тегов (иначе null).</summary>
    public double? Physical { get; init; }

    public string? Unit { get; init; }
}

/// <summary>Смежный блок регистров одного типа доступа, читаемый одним запросом.</summary>
public sealed record ReadBlock(ModbusAccess Access, ushort Start, ushort Quantity, IReadOnlyList<RegisterDef> Members);

/// <summary>Группирует регистры в минимальное число запросов.</summary>
public static class BatchPlanner
{
    // Ограничения Modbus на количество за один запрос.
    private const int MaxBits = 2000;
    private const int MaxRegisters = 125;

    public static IReadOnlyList<ReadBlock> Plan(IReadOnlyList<RegisterDef> registers, ushort maxGap = 0)
    {
        var blocks = new List<ReadBlock>();

        foreach (var group in registers.GroupBy(r => r.Access))
        {
            bool isBit = group.Key is ModbusAccess.Coil or ModbusAccess.DiscreteInput;
            int maxQty = isBit ? MaxBits : MaxRegisters;

            var ordered = group.OrderBy(r => r.Address).ToList();
            var current = new List<RegisterDef>();
            ushort blockStart = 0;
            ushort blockEnd = 0; // адрес за последним словом блока (эксклюзивно)

            foreach (var reg in ordered)
            {
                ushort regEnd = (ushort)(reg.Address + reg.Count);

                if (current.Count == 0)
                {
                    current.Add(reg);
                    blockStart = reg.Address;
                    blockEnd = regEnd;
                    continue;
                }

                bool contiguous = reg.Address <= blockEnd + maxGap;
                bool withinLimit = (regEnd - blockStart) <= maxQty;

                if (contiguous && withinLimit)
                {
                    current.Add(reg);
                    if (regEnd > blockEnd) blockEnd = regEnd;
                }
                else
                {
                    blocks.Add(Flush(group.Key, blockStart, blockEnd, current));
                    current = [reg];
                    blockStart = reg.Address;
                    blockEnd = regEnd;
                }
            }

            if (current.Count > 0)
                blocks.Add(Flush(group.Key, blockStart, blockEnd, current));
        }

        return blocks;
    }

    private static ReadBlock Flush(ModbusAccess access, ushort start, ushort end, List<RegisterDef> members) =>
        new(access, start, (ushort)(end - start), members.ToList());
}

/// <summary>Stateless-декодер: сырые данные регистра → <see cref="TagSample"/>.</summary>
public static class TagDecoder
{
    /// <summary>Декодирование из битового источника (Coil/DiscreteInput).</summary>
    public static TagSample FromBit(RegisterDef def, bool bit, DateTime tsUtc) =>
        Finish(def, bit ? 1L : 0L, tsUtc);

    /// <summary>Декодирование из регистрового источника (Holding/Input). Используется первое слово.</summary>
    public static TagSample FromRegisters(RegisterDef def, ReadOnlySpan<ushort> words, DateTime tsUtc)
    {
        long raw = def.RawType switch
        {
            RawType.Int16 => (short)words[0],
            _ => words[0], // UInt16 (и Bit тут не встречается)
        };
        return Finish(def, raw, tsUtc);
    }

    private static TagSample Finish(RegisterDef def, long raw, DateTime tsUtc)
    {
        if (def.IsMapped)
        {
            // Дискретная ветка: значение по таблице (null, если соответствия нет).
            object? mapped = def.ValueMap!.TryGetValue(raw, out var v) ? v : null;
            return new TagSample
            {
                Name = def.Name,
                TimestampUtc = tsUtc,
                RawNumeric = raw,
                Value = mapped,
                Physical = null,
                Unit = null,
            };
        }

        // Непрерывная ветка: физ = raw * scale + offset.
        double physical = raw * def.Scale + def.Offset;
        return new TagSample
        {
            Name = def.Name,
            TimestampUtc = tsUtc,
            RawNumeric = raw,
            Value = physical,
            Physical = physical,
            Unit = def.Unit,
        };
    }
}
