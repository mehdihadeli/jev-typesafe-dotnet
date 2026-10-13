using System.Text.Json.Serialization;

namespace Typesafe.Sdk;

public sealed class NoulAnswer : TypeSafeAnswer
{
    [JsonPropertyName("noul")]
    public double Noul { get; init; }
}