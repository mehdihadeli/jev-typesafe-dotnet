using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Jev.Typesafe.Sdk.Serialization;

internal static class QuestionPreparation
{
    private static readonly JsonSerializerOptions Options = new()
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    internal static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        JsonContent content => content.Value.DeepClone(),
        JsonNode node => node.DeepClone(),
        _ => JsonSerializer.SerializeToNode(value, value.GetType(), Options),
    };

    internal static JsonObject Serialize(TypeSafeQuestion question)
    {
        if (question is RawQuestion raw) return (JsonObject)raw.Value.DeepClone();
        var result = new JsonObject { ["type"] = question.Type };
        if (question.Instructions is not null) result["instructions"] = ToNode(question.Instructions);
        switch (question)
        {
            case Choice choice: result["criteria"] = ToNode(choice.Criteria); break;
            case Score score: result["criteria"] = ToNode(score.Criteria); break;
            case Noul { Criteria: { } criteria }:
                var noul = new JsonObject();
                if (criteria.HasTrue) noul["true"] = ToNode(criteria.True);
                if (criteria.HasFalse) noul["false"] = ToNode(criteria.False);
                result["criteria"] = noul;
                break;
        }
        return result;
    }

    internal static JsonObject Normalize(IReadOnlyDictionary<string, TypeSafeQuestion> questions)
    {
        if (questions is null || questions.Count == 0) throw new TypeSafeException("At least one question is required.");
        var result = new JsonObject();
        foreach (var pair in questions)
        {
            var name = pair.Key;
            if (pair.Value is null) throw InvalidQuestion(name);
            var question = Serialize(pair.Value);
            if (question["type"] is not JsonValue tag || !tag.TryGetValue<string>(out var type) || string.IsNullOrEmpty(type))
                throw InvalidQuestion(name);
            if (type is "choice" or "score")
            {
                if (!question.ContainsKey("criteria")) throw new TypeSafeException($"Question \"{name}\" requires \"criteria\".");
                if (question["criteria"] is null || question["criteria"] is JsonObject { Count: 0 }
                    || question["criteria"] is JsonArray { Count: 0 }
                    || question["criteria"] is JsonValue criteriaValue && criteriaValue.TryGetValue<string>(out var criteriaText) && criteriaText.Length == 0)
                    throw new TypeSafeException(type == "score"
                        ? $"Score question \"{name}\" has no criteria; at least one score is required."
                        : $"Choice question \"{name}\" has no criteria; at least one choice is required.");
            }
            if (type == "noul")
            {
                if (question["criteria"] is { } criteria && criteria is not JsonObject)
                    throw new TypeSafeException($"Noul question \"{name}\" criteria must be an object.");
                if (question["instructions"] is null && !(question["criteria"] is JsonObject descriptions && descriptions.Any(entry => entry.Value is not null)))
                    throw new TypeSafeException($"Noul question \"{name}\" has neither instructions nor criteria; at least one is required.");
            }
            result[name] = question;
        }
        return result;
    }

    private static TypeSafeException InvalidQuestion(string name) => new($"Question \"{name}\" must be a question object or a dictionary with a nonempty string \"type\".");
}