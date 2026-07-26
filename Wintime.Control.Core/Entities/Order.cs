using Wintime.Control.Core.Enums;
using Wintime.Control.Core.Exceptions;

namespace Wintime.Control.Core.Entities;

/// <summary>
/// Заказ — одна номенклатурная позиция (ProductType + Quantity). Уровень над заданиями:
/// Order → ShiftTask (1:N). Прогресс НЕ хранится (derive-on-read). См. ADR-0009, PZP-05.
/// </summary>
public class Order : BaseEntity
{
    public string Number { get; set; } = string.Empty;  // человекочитаемый, НЕ уникален (CRM/1С)
    public DateTime OrderDate { get; set; }              // дата заказа (Utc)
    public DateTime DueDate { get; set; }                // крайний срок (Utc)
    public Guid ProductTypeId { get; set; }              // изделие заказа
    public ProductType? ProductType { get; set; }
    public int Quantity { get; set; }                    // требуемое кол-во годных, > 0
    public OrderStatus Status { get; set; } = OrderStatus.Active;
    public string? Note { get; set; }

    public ICollection<ShiftTask> Tasks { get; set; } = new List<ShiftTask>();

    // ── Конечный автомат (ADR-0002: логика в сущности, DomainException → 400) ──
    // goodQuantity = Σ(ActualQuantity − DefectQuantity) по привязанным заданиям —
    // считает вызывающий код (derive-on-read), сущность прогресс не хранит.
    public void Complete(int goodQuantity)
    {
        EnsureStatus(OrderStatus.Active, "Заказ не активен");
        if (goodQuantity < Quantity)
            throw new DomainException("Недостаточно годных деталей для завершения заказа");
        Status = OrderStatus.Completed;
    }

    public void Cancel()
    {
        EnsureStatus(OrderStatus.Active, "Заказ не активен");
        Status = OrderStatus.Cancelled;
    }

    public void Reopen()
    {
        if (Status == OrderStatus.Active)
            throw new DomainException("Заказ уже активен");
        Status = OrderStatus.Active;
    }

    private void EnsureStatus(OrderStatus expected, string message)
    {
        if (Status != expected)
            throw new DomainException(message);
    }
}
