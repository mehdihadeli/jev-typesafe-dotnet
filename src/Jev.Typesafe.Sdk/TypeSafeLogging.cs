using System.Text.Json;

namespace Jev.Typesafe.Sdk;

public enum TypeSafeLogLevel { Debug, Info, Warning, Error, Off }
public sealed class TypeSafeLogEntry(TypeSafeLogLevel level, string message)
{
    public TypeSafeLogLevel Level { get; } = level;
    public string Message { get; } = message;
}

internal static class TypeSafeLogging
{
    internal static TypeSafeLogLevel Resolve(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "debug" => TypeSafeLogLevel.Debug,
        "info" => TypeSafeLogLevel.Info,
        "warn" or "warning" => TypeSafeLogLevel.Warning,
        "error" => TypeSafeLogLevel.Error,
        "off" => TypeSafeLogLevel.Off,
        _ => TypeSafeLogLevel.Warning,
    };

    internal static bool IsSecret(string header)
    {
        var name = header.ToLowerInvariant();
        return name is "authorization" or "proxy-authorization" or "x-api-key" or "api-key" or "cookie" or "set-cookie"
            || name.Contains("token") || name.Contains("secret");
    }

    internal static string Redact(string text, IReadOnlyDictionary<string, string> headers)
    {
        var values = headers.Where(pair => IsSecret(pair.Key)).SelectMany(pair =>
            pair.Value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || pair.Value.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase)
                ? new[] { pair.Value, pair.Value.Substring(pair.Value.IndexOf(' ') + 1) } : new[] { pair.Value });
        foreach (var value in values.Where(value => value.Length > 0).OrderByDescending(value => value.Length))
        {
            text = text.Replace(value, "***").Replace(Uri.EscapeDataString(value), "***");
            var escaped = JsonSerializer.Serialize(value);
            text = text.Replace(escaped.Substring(1, escaped.Length - 2), "***");
        }
        return text;
    }

    internal static Exception SafeException(Exception error, IReadOnlyDictionary<string, string> headers, int depth = 0) =>
        new Exception(Redact(error.Message, headers), depth < 8 && error.InnerException is { } inner ? SafeException(inner, headers, depth + 1) : null);
}