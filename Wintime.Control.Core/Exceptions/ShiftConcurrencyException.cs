namespace Wintime.Control.Core.Exceptions;

public sealed class ShiftConcurrencyException : Exception
{
    public ShiftConcurrencyException(string message) : base(message)
    {
    }
}
