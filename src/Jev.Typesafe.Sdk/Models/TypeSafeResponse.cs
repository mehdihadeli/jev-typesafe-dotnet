using System.Text.Json.Serialization;

namespace Jev.Typesafe.Sdk.Models;

public abstract class TypeSafeResponse
{
    private RawHttpResponse? _raw;

    [JsonIgnore]
    public RawHttpResponse RawHttpResponse => _raw
        ?? throw new TypeSafeException("The response was not created from a raw HTTP response.");

    [JsonIgnore]
    public string RequestId => RawHttpResponse.Headers.TryGetValue("x-typesafe-request-id", out var id)
        ? id : throw new TypeSafeException("The response did not include a request ID.");

    internal void Attach(RawHttpResponse raw) => _raw = raw;
}