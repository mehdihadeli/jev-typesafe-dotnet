using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jev.Typesafe.Sdk.Serialization;

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
            _ => new RawQuestion(System.Text.Json.Nodes.JsonNode.Parse(root.GetRawText())!.AsObject()),
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        TypeSafeQuestion value,
        JsonSerializerOptions options
    ) => QuestionPreparation.Serialize(value).WriteTo(writer, options);
}