using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Jev.Typesafe.Sdk.Models;

[JsonConverter(typeof(JsonContentConverter))]
public sealed class JsonContent
{
    public JsonNode Value { get; }

    private JsonContent(JsonNode value)
    {
        if (value.GetValueKind() is not (JsonValueKind.String or JsonValueKind.Object or JsonValueKind.Array))
            throw new TypeSafeException("JSON content must be a string, object, or array.");
        Value = value;
    }

    public static JsonContent From(object value) => new(QuestionPreparation.ToNode(value)
        ?? throw new TypeSafeException("State is required."));
    public static implicit operator JsonContent(string value) => From(value);
    public static implicit operator JsonContent(JsonObject value) => new(value);
    public static implicit operator JsonContent(JsonArray value) => new(value);
}

internal sealed class JsonContentConverter : JsonConverter<JsonContent>
{
    public override JsonContent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        JsonContent.From(JsonNode.Parse(ref reader)!);
    public override void Write(Utf8JsonWriter writer, JsonContent value, JsonSerializerOptions options) => value.Value.WriteTo(writer, options);
}