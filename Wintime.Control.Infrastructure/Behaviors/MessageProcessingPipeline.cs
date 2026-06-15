using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Infrastructure.Handlers;

namespace Wintime.Control.Infrastructure.Behaviors;

/// <summary>
/// Pipeline обработки полученного сообщения MQTT.
/// </summary>
/// <remarks>
/// Шаги конвейера внедряются через конструктор (scoped-сервисы), а не резолвятся
/// из <see cref="IServiceProvider"/> по месту — зависимости видны в сигнатуре и
/// подменяемы в тестах. Сам pipeline регистрируется как scoped и создаётся внутри
/// scope, открытого на одно сообщение.
/// </remarks>
/// <param name="decoder">Декодирование сырого payload в объект телеметрии.</param>
/// <param name="validator">Валидация типов датчиков и COV-фильтрация.</param>
/// <param name="store">Сохранение телеметрии в БД.</param>
/// <param name="statusUpdater">Обновление статуса ТПА по полю mode.</param>
/// <param name="cycleProcessor">Детектирование и сохранение циклов.</param>
public class MessageProcessingPipeline(
    IDecodeTelemetryDataHandler decoder,
    IValidateTelemetryDataHandler validator,
    IStoreTelemetryDataHandler store,
    IUpdateImmStatusHandler statusUpdater,
    ICycleProcessingHandler cycleProcessor)
{
    public async Task ProcessAsync(MqttProcessingContext context, CancellationToken ct)
    {
        // Сначала необходимо преобразовать данные из json в объект - декодировать.
        // На момент начала обработки context не заполнен полностью, заполнены только поля Topic и Payload и присвоен уникальный id в поле MessageId.
        // В поле Payload лежит именно сырые данные из сообщений MQTT, преобразованные в текст.
        // Мы ожидаем, что это будет текст в формате json.
        // Подходящий нам json имеет формат такого вида:
        // {"timestamp":"2026-05-04T00:22:25.3000648Z","mode":"auto","sensors":{"counter":0,"mode":"manual","sensor 1":25.0737, "sensor 2": 0.01, ... "sensor X-1":"false", "sensor X":"string"}}
        // Т.е. json всегда содержит обязательное поле timestamp, mode и массив sensors из пар "имя датчика": значение.
        // Значение может быть разных типов. Тип значения задаётся в шаблоне IMM.
        // Поле Topic содержит название топика. Формат имени топика такой: /control/imm/{deviceId}/telemetry/, где {deviceId} - это GUID назначенный устройству в нашей БД.
        // Устройство сначала регистрируется в БД и только после этого мы можем обрабатывать его сообщения.
        var (success, updatedContext) = await decoder.DecodeAsync(context);
        if (!success)
            return;
        context = updatedContext;

        // Validation - проверяем, что нам есть вообще что сохранять и что данные корректные (например, что привязан экземпляр оборудования).
        // Если получится, то исправим данные - привяжем экземпляр IMM.
        // Также здесь заполняется список показаний датчиков - в него включаются только те, которые указаны в шаблоне оборудования.
        // Здесь же применяется и COV-фильтрация.
        var (validationSuccess, validatedContext) = await validator.ValidateAsync(context);
        if (!validationSuccess)
            return;
        context = validatedContext;

        // Сохраняем полученное и обработанное сообщение в БД
        await store.SaveAsync(context);

        // Обновляем статус IMM на основе поля mode из сообщения
        await statusUpdater.UpdateStatusAsync(context);

        // Обрабатываем циклы: детектируем смыкания по cycleCounter, сохраняем цикл в БД,
        // обновляем выработку задания и ресурс пресс-формы
        await cycleProcessor.ProcessAsync(context, ct);
    }
}
