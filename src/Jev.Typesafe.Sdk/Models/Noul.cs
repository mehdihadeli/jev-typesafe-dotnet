using System.Text.Json.Serialization;

namespace Jev.Typesafe.Sdk.Models;

public sealed class Noul : Question
{
    public Noul() { }
    public Noul(object? instructions, NoulCriteria? criteria = null)
    {
        Instructions = instructions;
        Criteria = criteria;
    }
    public override string Type => "noul";

    [JsonPropertyName("criteria")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NoulCriteria? Criteria { get; init; }
}