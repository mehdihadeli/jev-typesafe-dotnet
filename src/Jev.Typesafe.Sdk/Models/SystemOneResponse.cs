using System.Text.Json.Serialization;

namespace Jev.Typesafe.Sdk.Models;

public class SystemOneResponse : TypeSafeResponse
{
    private IReadOnlyDictionary<string, NoulAnswer>? _nouls;
    private IReadOnlyDictionary<string, ChoiceAnswer>? _choices;
    private IReadOnlyDictionary<string, ScoreAnswer>? _scores;
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("answers")]
    public IReadOnlyDictionary<string, TypeSafeAnswer> Answers { get; init; } =
        new Dictionary<string, TypeSafeAnswer>();

    [JsonPropertyName("usage")]
    public Usage? Usage { get; init; }

    [JsonIgnore]
    public IReadOnlyDictionary<string, NoulAnswer> Nouls =>
        _nouls ??= Answers
            .Where(pair => pair.Value is NoulAnswer)
            .ToDictionary(pair => pair.Key, pair => (NoulAnswer)pair.Value);

    [JsonIgnore]
    public IReadOnlyDictionary<string, ChoiceAnswer> Choices =>
        _choices ??= Answers
            .Where(pair => pair.Value is ChoiceAnswer)
            .ToDictionary(pair => pair.Key, pair => (ChoiceAnswer)pair.Value);

    [JsonIgnore]
    public IReadOnlyDictionary<string, ScoreAnswer> Scores =>
        _scores ??= Answers
            .Where(pair => pair.Value is ScoreAnswer)
            .ToDictionary(pair => pair.Key, pair => (ScoreAnswer)pair.Value);

    [JsonIgnore]
    public IReadOnlyDictionary<string, System.Text.Json.JsonElement> UnmodeledAnswers { get; internal set; } =
        new Dictionary<string, System.Text.Json.JsonElement>();

    public static SystemOneResponse FromHttpResponse(RawHttpResponse response) => ResponseDecoder.Decode<SystemOneResponse>(response);
}