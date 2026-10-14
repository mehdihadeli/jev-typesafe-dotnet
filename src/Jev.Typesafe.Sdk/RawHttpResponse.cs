using System.Collections.ObjectModel;
using System.Text.Json;

namespace Jev.Typesafe.Sdk;

public sealed class RawHttpResponse
{
    public int Status { get; }
    public string Content { get; }
    public object? Body { get; }
    public IReadOnlyDictionary<string, string> Headers { get; }
    public string? Endpoint { get; }
    public bool IsSuccessStatusCode => Status >= 200 && Status < 300;

    public RawHttpResponse(int status, string content, IReadOnlyDictionary<string, string>? headers = null, string? endpoint = null)
    {
        Status = status;
        Content = content ?? throw new ArgumentNullException(nameof(content));
        Headers = new ReadOnlyDictionary<string, string>(headers is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : headers.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase));
        Endpoint = endpoint;
        try
        {
            using var document = JsonDocument.Parse(content);
            Body = document.RootElement.ValueKind == JsonValueKind.Null ? null : document.RootElement.Clone();
        }
        catch (JsonException) { Body = content.Length == 0 ? null : content; }
    }

    public static async Task<RawHttpResponse> FromAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        if (response is null) throw new ArgumentNullException(nameof(response));
        cancellationToken.ThrowIfCancellationRequested();
        var content = response.Content is null ? "" : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in response.Headers) headers[header.Key] = string.Join(", ", header.Value);
        if (response.Content is not null)
            foreach (var header in response.Content.Headers) headers[header.Key] = string.Join(", ", header.Value);
        return new RawHttpResponse((int)response.StatusCode, content, headers,
            response.RequestMessage?.RequestUri is { } uri ? FormatEndpoint(response.RequestMessage.Method, uri) : null);
    }

    internal static string FormatEndpoint(HttpMethod method, Uri uri)
    {
        var sanitized = new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" }.Uri;
        return method.Method + " " + sanitized.GetLeftPart(UriPartial.Path);
    }
}