using System.Text.Json;
using System.Text.Json.Serialization;

namespace Typesafe.Sdk;

internal sealed class TypeSafeQuestionConverter : JsonConverter<TypeSafeQuestion>
{
    public override TypeSafeQuestion Read(
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
            "noul" => root.Deserialize<Noul>(options)!,
            "choice" => root.Deserialize<Choice>(options)!,
            "score" => root.Deserialize<Score>(options)!,
            _ => throw new JsonException($"Unsupported TypeSafe question type '{type}'."),
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        TypeSafeQuestion value,
        JsonSerializerOptions options
    ) => JsonSerializer.Serialize(writer, (object)value, value.GetType(), options);
}