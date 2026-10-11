using System.Text.Json;
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

public sealed class Noul : TypeSafeQuestion
{
    public override string Type => "noul";

    [JsonPropertyName("criteria")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NoulCriteria? Criteria { get; init; }
}

public sealed class NoulCriteria
{
    [JsonPropertyName("true")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? True { get; init; }

    [JsonPropertyName("false")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? False { get; init; }
}

public sealed class Choice : TypeSafeQuestion
{
    public override string Type => "choice";

    [JsonPropertyName("criteria")]
    public required IReadOnlyDictionary<string, object?> Criteria { get; init; }
}

public sealed class Score : TypeSafeQuestion
{
    public override string Type => "score";

    [JsonPropertyName("criteria")]
    public required IReadOnlyList<object?> Criteria { get; init; }
}

public sealed class SystemOneRequest
{
    [JsonPropertyName("state")]
    public required object State { get; init; }

    [JsonPropertyName("model")]
    public string Model { get; init; } = "jev-latest";

    [JsonPropertyName("questions")]
    public required IReadOnlyDictionary<string, TypeSafeQuestion> Questions { get; init; }
}

public abstract class TypeSafeAnswer
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }
}

public sealed class NoulAnswer : TypeSafeAnswer
{
    [JsonPropertyName("noul")]
    public double Noul { get; init; }
}

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

public sealed class Usage
{
    [JsonPropertyName("input_tokens")]
    public int? InputTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    public int? OutputTokens { get; init; }
}

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

public sealed class ModelMetadata
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("release_date")]
    public required string ReleaseDate { get; init; }
}

public sealed class ModelMetadataList
{
    [JsonPropertyName("models")]
    public IReadOnlyList<ModelMetadata> Models { get; init; } = [];
}
