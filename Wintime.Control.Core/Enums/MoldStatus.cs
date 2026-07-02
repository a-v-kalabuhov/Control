namespace Wintime.Control.Core.Enums;

/// <summary>
/// Статус пресс-формы. Заглушка под РОСОМС (ROS-01): в фазе Мун поле nullable
/// и игнорируется (доступность ПФ определяется только флагом IsActive).
/// Значения фиксированы — хранятся в БД как int (колонка Molds.MoldStatus).
/// </summary>
public enum MoldStatus
{
    InWork = 0,        // В работе
    InRepair = 1,      // В ремонте
    Modernization = 2, // Модернизация
    Maintenance = 3    // ТО
}
