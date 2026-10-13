using System.Text.Json;
using System.Text.Json.Serialization;

namespace Typesafe.Sdk;

public sealed class ScoreAnswer : TypeSafeAnswer
{
    [JsonPropertyName("score")]
    public double Score { get; init; }

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    [JsonPropertyName("legend")]
    public IReadOnlyDictionary<int, JsonElement> Legend { get; init; } =
        new Dictionary<int, JsonElement>();

    [JsonPropertyName("probabilities")]
    public IReadOnlyDictionary<int, double> Probabilities { get; init; } =
        new Dictionary<int, double>();
}