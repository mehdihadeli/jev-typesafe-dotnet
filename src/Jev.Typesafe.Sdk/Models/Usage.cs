using System.Text.Json.Serialization;

namespace Jev.Typesafe.Sdk.Models;

public sealed class Usage
{
    [JsonPropertyName("input_tokens")]
    public int? InputTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    public int? OutputTokens { get; init; }
}