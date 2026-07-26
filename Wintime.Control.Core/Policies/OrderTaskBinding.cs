using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Exceptions;

namespace Wintime.Control.Core.Policies;

/// <summary>
/// Правило привязки задания к заказу (PZP-05, UC-6): заказ активен, у ПФ задания задан тип,
/// и тип ПФ совпадает с типом изделия заказа. Чистая функция — вызывающий код грузит сущности.
/// </summary>
public static class OrderTaskBinding
{
    public static void EnsureCanBind(Order order, Guid? moldProductTypeId)
    {
        if (order.Status != OrderStatus.Active)
            throw new DomainException("Привязать задание можно только к активному заказу");
        if (moldProductTypeId == null)
            throw new DomainException("У пресс-формы задания не задан тип изделия");
        if (moldProductTypeId.Value != order.ProductTypeId)
            throw new DomainException("Изделие задания не совпадает с изделием заказа");
    }
}
