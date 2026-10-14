namespace Jev.Typesafe.Sdk.Exceptions;

public class TypeSafeException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public class TypeSafeApiConnectionException(string message, Exception? innerException = null)
    : TypeSafeException(message, innerException);

public sealed class TypeSafeApiTimeoutException : TypeSafeApiConnectionException
{
    public TimeSpan Timeout { get; }

    public TypeSafeApiTimeoutException(TimeSpan timeout, Exception? innerException = null)
        : base($"Request timed out (timeout={FormatSeconds(timeout)}).", innerException) => Timeout = timeout;

    private static string FormatSeconds(TimeSpan timeout)
    {
        var value = timeout.TotalSeconds.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
        return value.Contains(".") || value.Contains("E") ? value : value + ".0";
    }
}