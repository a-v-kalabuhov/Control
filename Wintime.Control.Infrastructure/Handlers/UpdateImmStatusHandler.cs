using Microsoft.Extensions.Logging;
using Wintime.Control.Core.Constants;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Core.Interfaces;

namespace Wintime.Control.Infrastructure.Handlers;

/// <summary>
/// Обработчик конвейера телеметрии, обновляющий статус ТПА на основе
/// поля <c>Mode</c> входящего MQTT-сообщения.
/// </summary>
public class UpdateImmStatusHandler : IUpdateImmStatusHandler
{
    private readonly IImmStatusService _statusService;
    private readonly ILogger<UpdateImmStatusHandler> _logger;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="UpdateImmStatusHandler"/>.
    /// </summary>
    /// <param name="statusService">Сервис обновления и хранения статусов ТПА.</param>
    /// <param name="logger">Логгер обработчика.</param>
    public UpdateImmStatusHandler(IImmStatusService statusService, ILogger<UpdateImmStatusHandler> logger)
    {
        _statusService = statusService;
        _logger = logger;
    }

    /// <summary>
    /// Определяет статус ТПА по полю <c>Mode</c> и делегирует сохранение
    /// в <see cref="IImmStatusService.UpdateStatusAsync"/>.
    /// </summary>
    /// <param name="context">Контекст обработки MQTT-сообщения с данными устройства.</param>
    public async Task UpdateStatusAsync(MqttProcessingContext context)
    {
        var immId = context.Device!.Id;
        var status = MapModeToStatus(context.Data?.Mode);
        var changedAt = DateTimeOffset.FromUnixTimeSeconds(context.Data!.Timestamp).UtcDateTime;

        await _statusService.UpdateStatusAsync(immId, status, changedAt);
    }

    /// <summary>
    /// Преобразует строковое значение режима работы ТПА в статус.
    /// </summary>
    /// <param name="mode">Значение поля <c>Mode</c> из MQTT-сообщения (регистр не важен).</param>
    /// <returns>
    /// Один из статусов: <c>Auto</c>, <c>Manual</c>, <c>Alarm</c>, <c>Idle</c>;
    /// при неизвестном или отсутствующем значении — <c>Offline</c>.
    /// </returns>
    private static string MapModeToStatus(string? mode) => ImmMode.Normalize(mode) switch
    {
        ImmMode.Auto   => ImmStatus.Auto,
        ImmMode.Manual => ImmStatus.Manual,
        ImmMode.Alarm  => ImmStatus.Alarm,
        ImmMode.Idle   => ImmStatus.Idle,
        _              => ImmStatus.Offline
    };
}
