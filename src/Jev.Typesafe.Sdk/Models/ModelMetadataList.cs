using System.Text.Json.Serialization;

namespace Jev.Typesafe.Sdk.Models;

public class ModelMetadataList : TypeSafeResponse
{
    [JsonPropertyName("models")]
    public IReadOnlyList<ModelMetadata> Models { get; init; } = [];
}

public sealed class ListModelsResponse : ModelMetadataList
{
    public static ListModelsResponse FromHttpResponse(RawHttpResponse response) => ResponseDecoder.Decode<ListModelsResponse>(response);
}