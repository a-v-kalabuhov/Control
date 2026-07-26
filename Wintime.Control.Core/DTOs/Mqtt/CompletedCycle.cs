using Wintime.Control.Core.Entities;

namespace Wintime.Control.Core.DTOs.Mqtt;

/// <summary>
/// Уже сохранённый (Стадия 1) завершённый цикл + контекст для конвейера ICycleHandler.
/// </summary>
public record CompletedCycle(ImmCycle Cycle, ShiftTask? ActiveTask, string Mode);
