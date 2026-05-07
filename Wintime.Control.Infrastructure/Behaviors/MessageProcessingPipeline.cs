
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Infrastructure.Handlers;

namespace Wintime.Control.Infrastructure.Behaviors;

/// <summary>
/// Pipeline обработки полученного сообщений MQTT.
/// </summary>
/// <param name="sp"></param>
public class MessageProcessingPipeline(IServiceProvider sp)
{
    private readonly IServiceProvider _sp = sp;
    public async Task ProcessAsync(MqttProcessingContext context, CancellationToken ct)
    {
        /// Сначала необходимо преобразовать данные из json в объект - декодировать
        /// На момент начала обработки context не заполнен полностью, заполнены только поля Topic и Payload и присвоен уникальный id в поле MessageId.
        /// В поле Payload лежит именно сырые данные из сообщений MQTT, преобразованные в текст.
        /// Мы ожидаем, что это будет текст в формате json.
        /// Подходящий нам json имеет формат такого вида:
        /// {"timestamp":"2026-05-04T00:22:25.3000648Z","sensors":{"counter":0,"mode":"manual","sensor 1":25.0737, "sensor 2": 0.01, ... "sensor X-1":"false", "sensor X":"string"}}
        /// Т.е. json всегда содержит обязательное поле timestamp и массив sensors из пар "имя датчика": значение
        /// Значение может быть разных типов. Тип значения задаётся в шаблоне IMM.
        /// Поле Topic содержит название топика. Формат имени топика такой: /control/imm/{deviceId}/telemetry/, где {deviceId} - это GUID назначенный устройству в нашей БД.
        /// Устройство сначала регистрируется в БД и только после этго мы можем обрабатывать его сообщения.
        /// Необходимо в начале обработки проверить эти условия:
        /// 0. Формат имени топика соотвествут шаблону. Иначе пишем ошибку в лог и прекращаем обработку. 
        /// 0а. Значение deviceId из топика не пустое. Иначе пишем ошибку в лог и прекращаем обработку.
        /// 1. payload содержит текст в формате json, если формат не json - пишем ошибку в лог и прекращаем обработку.
        /// 2. json содержит поле timestamp и массив sensors, если таких полей нет - пишем ошибку в лог и прекращаем обработку.
        /// 3. массив sensors не пуст, если пуст - пишем ошибку в лог и прекращаем обработку.
        /// 4. массив sensors содержит две обязательные пары - "counter" и "mode", если их нет - пишем ошибку в лог и прекращаем обработку.
        /// 5. надо определить deviceId, проверить, существует ли такой в БД и записать его в поле context.Data.DeviceId
        /// 6. надо найти экземпляр ImmDto и записать его в поле context.Data.Imm, если экземпляра нет, то пишем ошибку в лог и прекращаем обработку.
        /// 7. Для найденного ImmDto надо найти шаблон (экземлпяр TemplateDto) и записать его в context.Data.Template. Если шаблона нет, то пишем ошибку в лог и прекращаем обработку.
        /// После этого можно продолжать обработку.        
        var decoder = _sp.GetRequiredService<IDecodeTelemetryDataHandler>();
        var (success, updatedContext) = await decoder.DecodeAsync(context);
        if (!success)
            return;
        context = updatedContext;

        // Validation - проверяем, что нам есть вообще что сохранять и что данные корректные (например, что привязан экземпляр оборудования)
        // Если получится, то исправим данные - привяжем экземпляр IMM.
        // Также здесь заполняется список показаний датчиков - в него включаются только те, которые указаны в шаблоне оборудования.
        // Здесь же можно применить и CovFilter.
        var validator = _sp.GetRequiredService<IValidateTelemetryDataHandler>();
        var (validationSuccess, validatedContext) = await validator.ValidateAsync(context);
        if (!validationSuccess)
            return;
        context = validatedContext;
        // Сохраняем полученное и обработанное сообщение в БД
        await _sp.GetRequiredService<IStoreTelemetryDataHandler>().SaveAsync(context);           
    }
}