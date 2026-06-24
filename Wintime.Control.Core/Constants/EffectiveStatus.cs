namespace Wintime.Control.Core.Constants;

/// <summary>
/// Эффективное состояние ТПА — производное от (сырой режим + статус активного задания +
/// наличие открытого простоя + признак прохождения порога). Вычисляется на лету, наружу
/// отдаётся фронтенду. 6 значений; «Stopped» наружу не выходит, «Alarm» растворяется.
/// </summary>
public static class EffectiveStatus
{
    public const string Production = "Production"; // Работа
    public const string Setup      = "Setup";      // Наладка
    public const string Downtime   = "Downtime";   // Простой
    public const string Unplanned  = "Unplanned";  // Работа без задания
    public const string NoTask     = "NoTask";     // Без задания
    public const string Offline    = "Offline";    // Нет связи
}
