using System.Text.Json.Serialization;

namespace Typesafe.Sdk;

public abstract class TypeSafeQuestion
{
    [JsonPropertyName("type")]
    public abstract string Type { get; }

    [JsonPropertyName("instructions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Instructions { get; init; }
}