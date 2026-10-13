using System.Text.Json.Serialization;

namespace Typesafe.Sdk;

public sealed class ChoiceAnswer : TypeSafeAnswer
{
    [JsonPropertyName("choice")]
    public required string Choice { get; init; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    [JsonPropertyName("probabilities")]
    public IReadOnlyDictionary<string, double> Probabilities { get; init; } =
        new Dictionary<string, double>();
}