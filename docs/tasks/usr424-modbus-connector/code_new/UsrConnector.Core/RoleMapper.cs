namespace UsrConnector.Core;

/// <summary>
/// Сводит декодированные значения регистров в <see cref="RoleSnapshot"/> по назначенным
/// ролям. Сигналы с ролью None публикуются в ExtraFields под своим именем — прозрачно,
/// без интерпретации (принцип: коннектор снимает, интерпретируют внешние системы).
/// </summary>
public static class RoleMapper
{
    public static RoleSnapshot Map(IReadOnlyList<RegisterSample> samples, DateTimeOffset ts)
    {
        bool? injection = null, injection2 = null;
        bool? mouldClosed = null, mouldOpened = null;
        bool? ejFwd = null, ejBwd = null, reject = null;
        double? injPos = null, injPos2 = null, moldPos = null;
        Dictionary<string, object?>? extra = null;

        foreach (var s in samples)
        {
            switch (s.Def.Role)
            {
                case SignalRole.Injection: injection = s.Bool; break;
                case SignalRole.Injection2: injection2 = s.Bool; break;
                case SignalRole.MouldClosed: mouldClosed = s.Bool; break;
                case SignalRole.MouldOpened: mouldOpened = s.Bool; break;
                case SignalRole.EjectorFwdReached: ejFwd = s.Bool; break;
                case SignalRole.EjectorBwdReached: ejBwd = s.Bool; break;
                case SignalRole.Reject: reject = s.Bool; break;
                case SignalRole.InjectionPosition: injPos = s.Value; break;
                case SignalRole.InjectionPosition2: injPos2 = s.Value; break;
                case SignalRole.MoldPosition: moldPos = s.Value; break;

                case SignalRole.None:
                    extra ??= new Dictionary<string, object?>();
                    extra[s.Def.Name] = (object?)s.Bool ?? s.Value;
                    break;
            }
        }

        return new RoleSnapshot
        {
            TimestampUtc = ts,
            ConnectionOk = true,
            Injection = injection,
            Injection2 = injection2,
            MouldClosed = mouldClosed,
            MouldOpened = mouldOpened,
            EjectorFwdReached = ejFwd,
            EjectorBwdReached = ejBwd,
            Reject = reject,
            InjectionPosition = injPos,
            InjectionPosition2 = injPos2,
            MoldPosition = moldPos,
            ExtraFields = extra,
        };
    }
}
