namespace UsrConnector.Core;

/// <summary>Режим работы ТПА.</summary>
public enum MachineMode
{
    /// <summary>Станок включён, связь есть, циклы не выполняются (нет впрыска дольше idle-таймаута).
    /// Состояние по умолчанию при старте и после восстановления связи.</summary>
    Idle,

    /// <summary>Полезная работа: цикл с впрыском. Устанавливается по фронту Injection.</summary>
    Auto,

    /// <summary>Цикл с впрыском начался, но не завершился штатно за alarm-таймаут
    /// (например, залипла пресс-форма).</summary>
    Alarm,

    /// <summary>Нет связи с устройством USR (или станок обесточен). Верхний слой в этом
    /// режиме не отправляет сообщения во внешние системы.</summary>
    Offline,
}

/// <summary>Исход последнего завершённого цикла.</summary>
public enum CycleCompletion
{
    /// <summary>Циклов ещё не было (с момента старта коннектора).</summary>
    NoneYet,

    /// <summary>Штатное завершение: дождались Ejector FWD reached (E1).</summary>
    Normal,

    /// <summary>Цикл прерван по таймауту (alarm): впрыск был, E1 не наступил вовремя.</summary>
    Aborted,

    /// <summary>Цикл прерван потерей связи с устройством.</summary>
    Interrupted,
}

/// <summary>
/// Состояние ТПА в данный момент — ПУБЛИЧНЫЙ КОНТРАКТ между ядром (нижний слой коннектора)
/// и хостом (верхний слой: шаблон устройства, упаковка сообщений, транспорт MQTT/REST).
///
/// Принцип: коннектор снимает данные и отдаёт их как есть; интерпретация — задача внешних
/// систем. Поэтому, например, подушка публикуется и для нештатно завершённых циклов —
/// у потребителя есть CycleCompletion, чтобы решить, как её учитывать.
/// </summary>
public sealed record MachineState
{
    /// <summary>Режим работы. Обязательное поле.</summary>
    public required MachineMode Mode { get; init; }

    /// <summary>
    /// Условный номер цикла. Обязательное поле. Инкрементируется по фронту впрыска
    /// (Injection ↑) — то есть считает НАГРУЗКУ НА ПРЕСС-ФОРМУ (каждый впрыск изнашивает
    /// форму), независимо от того, чем цикл закончился. Точная величина не важна — важно,
    /// что значение меняется на каждый цикл с впрыском.
    /// </summary>
    public required long CycleCounter { get; init; }

    /// <summary>Исход последнего завершённого цикла.</summary>
    public required CycleCompletion LastCycleCompletion { get; init; }

    /// <summary>Момент формирования состояния (по данным последнего опроса).</summary>
    public required DateTimeOffset TimestampUtc { get; init; }

    /// <summary>
    /// Необязательные поля («cushion», «moldPosition», температуры, данные чиллера и т.д.).
    /// Ключ — имя поля из шаблона устройства (совпадает с именем семантической роли для
    /// предопределённых сигналов). Состав определяется конфигурацией, а не кодом.
    /// </summary>
    public required IReadOnlyDictionary<string, object?> Fields { get; init; }
}

/// <summary>Стандартные имена опциональных полей в <see cref="MachineState.Fields"/>.</summary>
public static class WellKnownFields
{
    /// <summary>Подушка (double): минимум InjectionPosition за окно последнего завершённого цикла.</summary>
    public const string Cushion = "cushion";

    /// <summary>Подушка узла 2 (double): минимум InjectionPosition2 за то же окно цикла (2K-машина).</summary>
    public const string Cushion2 = "cushion2";

    /// <summary>Длительность последнего штатно завершённого цикла, мс (double).</summary>
    public const string LastCycleDurationMs = "lastCycleDurationMs";

    /// <summary>Вердикт качества последнего цикла (bool): true = брак (Reject).</summary>
    public const string Reject = "reject";

    /// <summary>Текущее положение плиты смыкания (double, в единицах калибровки).</summary>
    public const string MoldPosition = "moldPosition";

    /// <summary>Текущее положение шнека (double, в единицах калибровки).</summary>
    public const string InjectionPosition = "injectionPosition";
}
