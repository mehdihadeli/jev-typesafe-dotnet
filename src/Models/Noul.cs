using System.Text.Json.Serialization;

namespace Typesafe.Sdk;

public sealed class Noul : TypeSafeQuestion
{
    public override string Type => "noul";

    [JsonPropertyName("criteria")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NoulCriteria? Criteria { get; init; }
}