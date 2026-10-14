using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Jev.Typesafe.Sdk.Models;

public abstract class Question : TypeSafeQuestion
{
    public static implicit operator Question(JsonObject value) => new RawQuestion(value);
}

public sealed class RawQuestion(JsonObject value) : Question
{
    [JsonIgnore]
    public JsonObject Value { get; } = value ?? throw new ArgumentNullException(nameof(value));
    public override string Type => Value["type"] is JsonValue tag && tag.TryGetValue<string>(out var type) ? type : "";
}