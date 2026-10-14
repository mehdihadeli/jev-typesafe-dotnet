using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jev.Typesafe.Sdk.Serialization;

internal sealed class TypeSafeAnswerConverter : JsonConverter<TypeSafeAnswer>
{
    public override TypeSafeAnswer Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var type = root.GetProperty("type").GetString();
        return type switch
        {
            "noul" => root.Deserialize<NoulAnswer>(options)!,
            "choice" => root.Deserialize<ChoiceAnswer>(options)!,
            "score" => root.Deserialize<ScoreAnswer>(options)!,
            _ => throw new JsonException($"Unsupported TypeSafe answer type '{type}'."),
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        TypeSafeAnswer value,
        JsonSerializerOptions options
    ) => JsonSerializer.Serialize(writer, (object)value, value.GetType(), options);
}