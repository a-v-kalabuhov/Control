using Wintime.Control.Core.DTOs.Mqtt;
using SystemTask = System.Threading.Tasks.Task;

namespace Wintime.Control.Infrastructure.Handlers;

/// <summary>
/// Шаг конвейера обработки завершённого цикла (Стадия 2). Исполняется поверх
/// уже сохранённого ImmCycle; сбой одного шага не должен ронять остальные.
/// </summary>
public interface ICycleHandler
{
    SystemTask HandleAsync(CompletedCycle cycle, CancellationToken ct = default);
}
