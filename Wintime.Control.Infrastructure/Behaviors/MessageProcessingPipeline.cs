
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wintime.Control.Core.DTOs.Mqtt;
using Wintime.Control.Infrastructure.Handlers;

namespace Wintime.Control.Infrastructure.Behaviors;
public class MessageProcessingPipeline(IServiceProvider sp)
{
    private readonly IServiceProvider _sp = sp;
    public async Task ProcessAsync(MqttProcessingContext context, CancellationToken ct)
    {
        // Сначала необходимо преобразовать данные из json в объект
        var decoder = _sp.GetRequiredService<IDecodeTelemetryDataHandler>();
        if (!await decoder.DecodeAsync(context))
            return;
        // Validation - проверяем, что нам есть вообще то сохранять и что данные корректные (например, что привязан экземпляр оборудования)
        // если получится, то исправим данные - привяжем экземпляр
        // Также здесь заполняется список показаний датчиков - в него включаются только те, которые указаны в шаблоне оборудования.
        // Здесь же можно применить и CovFilter.
        var validator = _sp.GetRequiredService<IValidateTelemetryDataHandler>();
        if (!await validator.ValidateAsync(context))
            return;
        // Сохраняем полученное и обработанное сообщение в БД
        await _sp.GetRequiredService<IStoreTelemetryDataHandler>()
            .SaveAsync(context);           
    }
}