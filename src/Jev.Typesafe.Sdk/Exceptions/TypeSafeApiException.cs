using System.Net;
using System.Text.Json;

namespace Jev.Typesafe.Sdk.Exceptions;

public class TypeSafeApiException : TypeSafeException
{
    public int Status { get; }
    public HttpStatusCode StatusCode => (HttpStatusCode)Status;
    public object? Body { get; }
    public IReadOnlyDictionary<string, string> Headers { get; }
    public string? Endpoint { get; }
    public string? RequestId => Headers.TryGetValue("x-typesafe-request-id", out var id) ? id : null;
    public TimeSpan? RetryAfter => RetryAfterParser.Parse(Headers);
    public virtual string? FieldPath => null;
    public string ResponseBody { get; }

    public TypeSafeApiException(HttpStatusCode statusCode, string responseBody)
        : this(new RawHttpResponse((int)statusCode, responseBody)) { }

    public TypeSafeApiException(int status, object? body, IReadOnlyDictionary<string, string>? headers = null,
        string? message = null, string? endpoint = null)
        : this(new RawHttpResponse(status, body is string text ? text : JsonSerializer.Serialize(body), headers, endpoint), message) { }

    public TypeSafeApiException(RawHttpResponse response, string? message = null)
        : base(FormatMessage(response, message))
    {
        Status = response.Status;
        Body = response.Body;
        Headers = response.Headers;
        Endpoint = response.Endpoint;
        ResponseBody = response.Content;
    }

    internal static TypeSafeApiException FromResponse(RawHttpResponse response) => response.Status switch
    {
        400 => new TypeSafeBadRequestException(response),
        401 => new TypeSafeAuthenticationException(response),
        403 => new TypeSafePermissionDeniedException(response),
        404 => new TypeSafeNotFoundException(response),
        422 => new TypeSafeUnprocessableEntityException(response),
        429 => new TypeSafeRateLimitException(response),
        >= 500 => new TypeSafeInternalServerException(response),
        _ => new TypeSafeApiException(response),
    };

    private static string FormatMessage(RawHttpResponse response, string? message)
    {
        if (message is null)
        {
            message = ExtractMessage(response.Body);
            if (string.IsNullOrEmpty(message))
            {
                var fallback = response.Body switch
                {
                    null => "status code (no body)",
                    string text => text,
                    _ => JsonSerializer.Serialize(response.Body),
                };
                message = fallback.Length > 200 ? fallback.Substring(0, 200) + "\u2026" : fallback;
            }
        }
        var result = string.IsNullOrEmpty(message) ? response.Status.ToString() : $"{response.Status} {message}";
        if (response.Endpoint is not null) result = response.Endpoint + ": " + result;
        if (response.Headers.TryGetValue("x-typesafe-request-id", out var id)) result += $" (request_id={id})";
        return result;
    }

    private static string? ExtractMessage(object? body)
    {
        if (body is string text) return text;
        if (body is not JsonElement element || element.ValueKind != JsonValueKind.Object) return null;
        if (element.TryGetProperty("error", out var error))
        {
            if (error.ValueKind == JsonValueKind.String) return error.GetString();
            if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var nested)
                && nested.ValueKind == JsonValueKind.String) return nested.GetString();
        }
        if (element.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String) return message.GetString();
        if (!element.TryGetProperty("detail", out var detail)) return null;
        if (detail.ValueKind == JsonValueKind.String) return detail.GetString();
        if (detail.ValueKind == JsonValueKind.Object && detail.TryGetProperty("message", out var detailMessage)
            && detailMessage.ValueKind == JsonValueKind.String) return detailMessage.GetString();
        if (detail.ValueKind != JsonValueKind.Array) return null;
        var parts = new List<string>();
        foreach (var entry in detail.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("msg", out var value)
                || value.ValueKind != JsonValueKind.String) continue;
            var path = entry.TryGetProperty("loc", out var location) && location.ValueKind == JsonValueKind.Array
                ? string.Join(".", location.EnumerateArray().Where(item => item.ToString() != "body").Select(item => item.ToString())) : "";
            parts.Add(path.Length == 0 ? value.GetString()! : path + ": " + value.GetString());
        }
        return parts.Count == 0 ? null : string.Join("; ", parts);
    }
}

public sealed class TypeSafeBadRequestException(RawHttpResponse response) : TypeSafeApiException(response);
public sealed class TypeSafeAuthenticationException(RawHttpResponse response) : TypeSafeApiException(response);
public sealed class TypeSafePermissionDeniedException(RawHttpResponse response) : TypeSafeApiException(response);
public sealed class TypeSafeNotFoundException(RawHttpResponse response) : TypeSafeApiException(response);
public sealed class TypeSafeUnprocessableEntityException(RawHttpResponse response) : TypeSafeApiException(response);
public sealed class TypeSafeRateLimitException(RawHttpResponse response) : TypeSafeApiException(response);
public sealed class TypeSafeInternalServerException(RawHttpResponse response) : TypeSafeApiException(response);

public sealed class TypeSafeApiResponseValidationException : TypeSafeApiException
{
    public override string FieldPath { get; }
    public TypeSafeApiResponseValidationException(RawHttpResponse response, string fieldPath)
        : base(response, $"Invalid response data at {PythonRepresentation.Quote(fieldPath)}.") => FieldPath = fieldPath;
}

internal static class PythonRepresentation
{
    internal static string Quote(string text)
    {
        var quote = text.Contains("'") && !text.Contains("\"") ? "\"" : "'";
        return quote + text.Replace("\\", "\\\\").Replace(quote, "\\" + quote)
            .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t") + quote;
    }
}