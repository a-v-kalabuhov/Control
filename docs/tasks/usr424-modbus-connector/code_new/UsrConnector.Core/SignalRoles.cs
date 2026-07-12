namespace UsrConnector.Core;

/// <summary>
/// Семантический тип (роль) сигнала ТПА. Определяет смысл сигнала для логики состояния
/// машины, независимо от того, на каком регистре/входе он физически снят.
///
/// Роли назначаются регистрам в конфигурации. Прикладная логика (автомат состояния,
/// контроль подушки) обращается к сигналам ТОЛЬКО по ролям — адреса, функциональные коды
/// и калибровка скрыты нижним слоем. Эти же имена ролей используются как идентификаторы
/// переменных в шаблоне устройства для внешних систем.
/// </summary>
public enum SignalRole
{
    /// <summary>Без роли: сигнал читается и публикуется как опциональное поле, в логике не участвует.</summary>
    None = 0,

    // --- Дискретные роли (bool) ---

    /// <summary>Команда впрыска (Injection 1). ЯКОРЬ ЦИКЛА: фронт = начало цикла, инкремент счётчика износа.</summary>
    Injection,

    /// <summary>Статус «форма закрыта» (Mould closed, сухой контакт KA23).</summary>
    MouldClosed,

    /// <summary>Статус «форма полностью открыта» (Mould openend, DO570 DO1 → KA24).</summary>
    MouldOpened,

    /// <summary>Статус «толкатель достиг переднего положения» (Ejector FWD reached, KA27).
    /// Штатное завершение цикла (E1).</summary>
    EjectorFwdReached,

    /// <summary>Статус «толкатель вернулся в заднее положение» (Ejector BWD reached, KA26).</summary>
    EjectorBwdReached,

    /// <summary>Вердикт качества от контроллера: текущая деталь — брак (Reject, KA34).</summary>
    Reject,

    // --- Аналоговые роли (double) ---

    /// <summary>Положение шнека узла впрыска (Injection Position, AI2/LT3 или AI6/LT6).
    /// Подушка = минимум этого сигнала за окно цикла.</summary>
    InjectionPosition,

    /// <summary>Положение плиты смыкания (Mold Position, AI0/LT1).</summary>
    MoldPosition,

    // --- Роли узла 2 (двухузловая машина, 2K) ---

    /// <summary>Команда впрыска узла 2 (Injection 2, DO46). По модели 2K узлы работают
    /// синхронно; якорём цикла остаётся Injection узла 1.</summary>
    /// <remarks>Команда впрыска узла 2 (Injection 2). В автомате НЕ участвует (якорь цикла —
    /// впрыск узла 1: узел смыкания один, цикл один); читается и публикуется.</remarks>
    Injection2,

    /// <summary>Положение шнека узла 2 (Injection2 Position, AI6/LT6).
    /// Вторая подушка = минимум за то же окно цикла.</summary>
    InjectionPosition2,

}

/// <summary>
/// Снимок значений сигналов по ролям на один опрос — вход автомата состояния.
/// Формируется нижним слоем (маппинг регистры → роли); автомат не знает об адресах.
/// Отсутствующая роль = null (роль не назначена в конфигурации или чтение не удалось).
/// </summary>
public sealed record RoleSnapshot
{
    /// <summary>Момент получения данных от устройства (после ответа на опрос).</summary>
    public required DateTimeOffset TimestampUtc { get; init; }

    /// <summary>Связь с устройством: false = опрос не удался (устройство недоступно).</summary>
    public required bool ConnectionOk { get; init; }

    public bool? Injection { get; init; }
    public bool? MouldClosed { get; init; }
    public bool? MouldOpened { get; init; }
    public bool? EjectorFwdReached { get; init; }
    public bool? EjectorBwdReached { get; init; }
    public bool? Reject { get; init; }

    public double? InjectionPosition { get; init; }
    public double? MoldPosition { get; init; }

    public bool? Injection2 { get; init; }
    public double? InjectionPosition2 { get; init; }

    /// <summary>
    /// Дополнительные поля вне предопределённых ролей (имя поля из шаблона → значение).
    /// Пробрасываются в MachineState.Fields без интерпретации.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? ExtraFields { get; init; }

    /// <summary>Снимок «нет связи» — единственные валидные данные при недоступном устройстве.</summary>
    public static RoleSnapshot Disconnected(DateTimeOffset ts) =>
        new() { TimestampUtc = ts, ConnectionOk = false };
}
