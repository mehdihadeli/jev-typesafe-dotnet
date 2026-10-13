using System.Text.Json.Serialization;

namespace Typesafe.Sdk;

public sealed class SystemOneRequest
{
    [JsonPropertyName("state")]
    public required object State { get; init; }

    [JsonPropertyName("model")]
    public string Model { get; init; } = "jev-latest";

    [JsonPropertyName("questions")]
    public required IReadOnlyDictionary<string, TypeSafeQuestion> Questions { get; init; }
}