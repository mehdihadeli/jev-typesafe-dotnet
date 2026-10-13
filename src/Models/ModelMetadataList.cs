using System.Text.Json.Serialization;

namespace Typesafe.Sdk;

public sealed class ModelMetadataList
{
    [JsonPropertyName("models")]
    public IReadOnlyList<ModelMetadata> Models { get; init; } = [];
}