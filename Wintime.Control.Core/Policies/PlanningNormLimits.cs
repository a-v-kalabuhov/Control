using Wintime.Control.Core.Exceptions;

namespace Wintime.Control.Core.Policies;

/// <summary>
/// Пределы норматива наладки и буфера выпуска (PZP-14). Общие для умолчаний
/// (PlanningDefaults) и задания (ShiftTask).
/// </summary>
public static class PlanningNormLimits
{
    public const int MaxMinutes = 1440;
    public const int MaxPercent = 100;

    public static void EnsureSetupMinutes(int minutes)
    {
        if (minutes is < 0 or > MaxMinutes)
            throw new DomainException("Норматив наладки должен быть от 0 до 1440 минут");
    }

    public static void EnsureBuffer(int fixedMinutes, int percent)
    {
        if (fixedMinutes is < 0 or > MaxMinutes)
            throw new DomainException("Фиксированная часть буфера должна быть от 0 до 1440 минут");
        if (percent is < 0 or > MaxPercent)
            throw new DomainException("Процент буфера должен быть от 0 до 100");
    }
}
