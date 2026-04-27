namespace Wintime.Control.Emulator.Services;

using Refit;
using Wintime.Control.Emulator.Models;

/// <summary>
/// API для работы с Wintime.Control.API - основным веб-сервисом системы.
/// Эмулятор должен получать от него список подключенного оборудования (IMM).
/// Далее эмулятор создаёт инстансы IMM и запускает их.
/// А инстансы генерируют данные и отправляют в MQTT, эмулируя работу IMM.
/// </summary>
public interface IImmApiClient
{
    /// <summary>
    /// Этот метод запрашивает список IMM.
    /// </summary>
    /// <param name="ct"></param>
    /// <returns>Список объектов ImmDto. Каждый объект описывает IMM, поклченную к основному сервису.</returns>
    /// <remarks>
    /// Объект ImmDto содержит свойство TemplateId. Оно указывает на шаблон, который используется для создания IMM.
    /// Поэтому надо отдельно получать от основного сервиса информацию о шаблоне.
    /// </remarks>
    [Get("/api/imm")]
    Task<List<ImmDto>> GetImmsAsync([Authorize] CancellationToken ct);

    /// <summary>
    /// Получает шаблон IMM по его идентификатору.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="ct"></param>
    /// <returns>Объект TemplateDto или null</returns>
    [Get("/api/templates/{id}")]
    Task<TemplateDto> GetTemplateAsync(string id, [Authorize] CancellationToken ct);
}