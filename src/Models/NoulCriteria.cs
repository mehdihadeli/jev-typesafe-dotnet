using System.Text.Json.Serialization;

namespace Typesafe.Sdk;

public sealed class NoulCriteria
{
    [JsonPropertyName("true")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? True { get; init; }

    [JsonPropertyName("false")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? False { get; init; }
}