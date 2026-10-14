using System.Text.Json.Serialization;

namespace Jev.Typesafe.Sdk.Models;

public sealed class Choice : Question
{
    public Choice() { }
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public Choice(IEnumerable<string> criteria, object? instructions = null)
    {
        Criteria = criteria.ToDictionary(label => label, _ => (object?)null);
        Instructions = instructions;
    }
    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public Choice(IReadOnlyDictionary<string, JsonContent?> criteria, object? instructions = null)
    {
        Criteria = criteria.ToDictionary(pair => pair.Key, pair => (object?)pair.Value);
        Instructions = instructions;
    }
    public override string Type => "choice";

    [JsonPropertyName("criteria")]
    public required IReadOnlyDictionary<string, object?> Criteria { get; init; }
}