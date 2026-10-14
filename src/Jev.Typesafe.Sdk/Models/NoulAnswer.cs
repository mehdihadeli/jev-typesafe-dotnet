using System.Text.Json.Serialization;

namespace Jev.Typesafe.Sdk.Models;

public sealed class NoulAnswer : TypeSafeAnswer
{
    [JsonPropertyName("noul")]
    public double Noul { get; init; }
}