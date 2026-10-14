using System.Text.Json.Serialization;

namespace Jev.Typesafe.Sdk.Models;

public abstract class TypeSafeAnswer
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }
}