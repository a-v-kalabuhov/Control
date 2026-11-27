namespace Wintime.Control.Core.Exceptions;

public sealed class TelemetryCleanupTimeoutException : Exception
{
    public TelemetryCleanupTimeoutException(string message) : base(message) { }
}
