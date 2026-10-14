using System.Text.Json.Serialization;

namespace Jev.Typesafe.Sdk.Models;

public sealed class Score : Question
{
    public Score() { }
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public Score(IEnumerable<object?> criteria, object? instructions = null)
    {
        Criteria = criteria.ToArray();
        if (Criteria.Count == 0 || Criteria.Any(value => value is null))
            throw new TypeSafeException("Score criteria must contain at least one non-null description.");
        Instructions = instructions;
    }
    public override string Type => "score";

    [JsonPropertyName("criteria")]
    public required IReadOnlyList<object?> Criteria { get; init; }
}