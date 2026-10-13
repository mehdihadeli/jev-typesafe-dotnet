using System.Text.Json.Serialization;

namespace Typesafe.Sdk;

public sealed class Score : TypeSafeQuestion
{
    public override string Type => "score";

    [JsonPropertyName("criteria")]
    public required IReadOnlyList<object?> Criteria { get; init; }
}