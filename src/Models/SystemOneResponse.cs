using System.Text.Json.Serialization;

namespace Typesafe.Sdk;

public sealed class SystemOneResponse
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("answers")]
    public IReadOnlyDictionary<string, TypeSafeAnswer> Answers { get; init; } =
        new Dictionary<string, TypeSafeAnswer>();

    [JsonPropertyName("usage")]
    public Usage? Usage { get; init; }

    public IReadOnlyDictionary<string, NoulAnswer> Nouls =>
        Answers
            .Where(pair => pair.Value is NoulAnswer)
            .ToDictionary(pair => pair.Key, pair => (NoulAnswer)pair.Value);

    public IReadOnlyDictionary<string, ChoiceAnswer> Choices =>
        Answers
            .Where(pair => pair.Value is ChoiceAnswer)
            .ToDictionary(pair => pair.Key, pair => (ChoiceAnswer)pair.Value);

    public IReadOnlyDictionary<string, ScoreAnswer> Scores =>
        Answers
            .Where(pair => pair.Value is ScoreAnswer)
            .ToDictionary(pair => pair.Key, pair => (ScoreAnswer)pair.Value);
}