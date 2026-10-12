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
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedById { get; set; }

    public void Update(int setupMinutes, int bufferFixedMinutes, int bufferPercent,
                       string? userId, DateTime nowUtc)
    {
        PlanningNormLimits.EnsureSetupMinutes(setupMinutes);
        PlanningNormLimits.EnsureBuffer(bufferFixedMinutes, bufferPercent);

        SetupMinutes = setupMinutes;
        ReleaseBufferFixedMinutes = bufferFixedMinutes;
        ReleaseBufferPercent = bufferPercent;
        UpdatedAt = nowUtc;
        UpdatedById = userId;
    }
}
