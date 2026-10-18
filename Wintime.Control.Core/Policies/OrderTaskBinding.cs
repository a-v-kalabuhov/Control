using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Exceptions;

namespace Wintime.Control.Core.Policies;

/// <summary>
/// Правило привязки задания к заказу (PZP-05, PZP-19): заказ активен, изделие задания совпадает
/// с изделием заказа. Чистая функция — вызывающий код грузит сущности.
/// </summary>
public static class OrderTaskBinding
{
    public static void EnsureCanBind(Order order, Guid taskProductId)
    {
        if (order.Status != OrderStatus.Active)
            throw new DomainException("Привязать задание можно только к активному заказу");
        if (taskProductId != order.ProductId)
            throw new DomainException("Изделие задания не совпадает с изделием заказа");
    }
}
