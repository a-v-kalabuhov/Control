using Wintime.Control.Core.Policies;

namespace Wintime.Control.Core.Entities;

/// <summary>
/// Умолчания планирования (PZP-14, ADR-0015): одна строка, <c>Id = 1</c>. Копируются в
/// новые задания; смена умолчаний существующие задания не трогает.
/// </summary>
public class PlanningDefaults
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;
    public int SetupMinutes { get; set; }
    public int ReleaseBufferFixedMinutes { get; set; }
    public int ReleaseBufferPercent { get; set; }
    /// <summary>
    /// Порог конфликта плана, мин (PZP-07b, ADR-0022): пересечение двух заданий длиной не больше порога — не конфликт.
    /// В отличие от норматива и буфера, не снапшотится в задание — действует глобально и сразу.
    /// </summary>
    public const int DefaultConflictThresholdMinutes = 20;

    public int ConflictThresholdMinutes { get; set; } = DefaultConflictThresholdMinutes;
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedById { get; set; }

    public void Update(int setupMinutes, int bufferFixedMinutes, int bufferPercent,
                       string? userId, DateTime nowUtc, int? conflictThresholdMinutes = null)
    {
        PlanningNormLimits.EnsureSetupMinutes(setupMinutes);
        PlanningNormLimits.EnsureBuffer(bufferFixedMinutes, bufferPercent);
        if (conflictThresholdMinutes is int threshold)
            PlanningNormLimits.EnsureConflictThreshold(threshold);

        SetupMinutes = setupMinutes;
        ReleaseBufferFixedMinutes = bufferFixedMinutes;
        ReleaseBufferPercent = bufferPercent;
        if (conflictThresholdMinutes is int t)
            ConflictThresholdMinutes = t;
        UpdatedAt = nowUtc;
        UpdatedById = userId;
    }
}
