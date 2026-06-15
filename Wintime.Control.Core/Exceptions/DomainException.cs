namespace Wintime.Control.Core.Exceptions;

/// <summary>
/// Нарушение доменного инварианта или недопустимый переход состояния.
/// Маппится на HTTP 400 глобальным обработчиком — сообщение предназначено
/// для показа пользователю.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }
}
