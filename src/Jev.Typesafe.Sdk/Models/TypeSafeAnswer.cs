using System.Text.Json.Serialization;

namespace Jev.Typesafe.Sdk.Models;

[JsonConverter(typeof(TypeSafeAnswerConverter))]
public abstract class TypeSafeAnswer
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }
}