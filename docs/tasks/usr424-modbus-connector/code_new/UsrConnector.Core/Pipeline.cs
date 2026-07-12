namespace UsrConnector.Core;

/// <summary>Мгновенное декодированное значение одного регистра (stateless).</summary>
public sealed record RegisterSample
{
    public required RegisterDef Def { get; init; }

    /// <summary>Дискретное значение (для битовых ролей), с учётом Invert.</summary>
    public bool? Bool { get; init; }

    /// <summary>Непрерывное значение: raw * Scale + Offset.</summary>
    public double? Value { get; init; }
}

/// <summary>Смежный блок регистров одного типа доступа — один Modbus-запрос.</summary>
public sealed record ReadBlock(ModbusAccess Access, ushort Start, ushort Quantity, IReadOnlyList<RegisterDef> Members);

/// <summary>Группирует регистры в минимальное число запросов (лимиты Modbus: 125 слов / 2000 бит).</summary>
public static class BatchPlanner
{
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
            ushort blockStart = 0, blockEnd = 0;

            foreach (var reg in ordered)
            {
                ushort regEnd = (ushort)(reg.Address + reg.Count);

                if (current.Count == 0)
                {
                    current.Add(reg); blockStart = reg.Address; blockEnd = regEnd;
                    continue;
                }

                bool contiguous = reg.Address <= blockEnd + maxGap;
                bool withinLimit = regEnd - blockStart <= maxQty;

                if (contiguous && withinLimit)
                {
                    current.Add(reg);
                    if (regEnd > blockEnd) blockEnd = regEnd;
                }
                else
                {
                    blocks.Add(new ReadBlock(group.Key, blockStart, (ushort)(blockEnd - blockStart), current.ToList()));
                    current = [reg]; blockStart = reg.Address; blockEnd = regEnd;
                }
            }

            if (current.Count > 0)
                blocks.Add(new ReadBlock(group.Key, blockStart, (ushort)(blockEnd - blockStart), current.ToList()));
        }

        return blocks;
    }
}

/// <summary>Stateless-декодер сырых данных в <see cref="RegisterSample"/>.</summary>
public static class RegisterDecoder
{
    public static RegisterSample FromBit(RegisterDef def, bool bit) =>
        new() { Def = def, Bool = def.Invert ? !bit : bit };

    public static RegisterSample FromWords(RegisterDef def, ReadOnlySpan<ushort> words)
    {
        long raw = def.RawType switch
        {
            RawType.Int16 => (short)words[0],
            _ => words[0],
        };
        return new RegisterSample { Def = def, Value = raw * def.Scale + def.Offset };
    }
}
