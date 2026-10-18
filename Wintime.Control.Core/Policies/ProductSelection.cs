using Wintime.Control.Core.Entities;
using Wintime.Control.Core.Exceptions;

namespace Wintime.Control.Core.Policies;

/// <summary>
/// Можно ли выбрать изделие в новом заказе/задании (PZP-19): изделие и его тип активны;
/// для задания — изделие принадлежит типу ПФ. Чистые функции; вызывающий код грузит ProductType.
/// </summary>
public static class ProductSelection
{
    public static void EnsureSelectable(Product product)
    {
        if (product.ProductType == null)
            throw new InvalidOperationException("Product.ProductType должен быть загружен (Include)");
        if (!product.IsActive)
            throw new DomainException("Изделие в архиве");
        if (!product.ProductType.IsActive)
            throw new DomainException("Тип изделия в архиве");
    }

    public static void EnsureBelongsToMold(Product product, Guid? moldProductTypeId)
    {
        if (moldProductTypeId == null)
            throw new DomainException("У пресс-формы не задан тип изделия");
        if (product.ProductTypeId != moldProductTypeId.Value)
            throw new DomainException("Изделие не относится к типу изделия пресс-формы");
    }
}
