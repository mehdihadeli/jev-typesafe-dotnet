using System.Text.Json.Serialization;

namespace Typesafe.Sdk;

public sealed class Choice : TypeSafeQuestion
{
    public override string Type => "choice";

    [JsonPropertyName("criteria")]
    public required IReadOnlyDictionary<string, object?> Criteria { get; init; }
}