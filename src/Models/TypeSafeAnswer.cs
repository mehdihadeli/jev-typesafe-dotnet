using System.Text.Json.Serialization;

namespace Typesafe.Sdk;

public abstract class TypeSafeAnswer
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }
}