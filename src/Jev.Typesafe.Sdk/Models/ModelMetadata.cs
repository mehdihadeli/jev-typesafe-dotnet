using System.Text.Json.Serialization;

namespace Jev.Typesafe.Sdk.Models;

public sealed class ModelMetadata
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("release_date")]
    public required string ReleaseDate { get; init; }
}