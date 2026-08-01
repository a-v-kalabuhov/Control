using Wintime.Control.Core.Entities;

namespace Wintime.Control.Core.DTOs.Mqtt;

/// <summary>
/// Уже сохранённый (Стадия 1) завершённый цикл + контекст для конвейера ICycleHandler.
/// </summary>
/// <param name="EndedByCounter">
/// Цикл закрыт инкрементом счётчика (а не только сменой режима из auto).
/// Счётчик растёт ровно один раз на физический цикл — только такие циклы
/// засчитываются в выпуск задания (см. CycleProcessingPolicy.ShouldCountOutput).
/// По умолчанию true — большинство вызовов в тестах моделируют настоящий цикл.
/// </param>
public record CompletedCycle(ImmCycle Cycle, ShiftTask? ActiveTask, string Mode, bool EndedByCounter = true);
