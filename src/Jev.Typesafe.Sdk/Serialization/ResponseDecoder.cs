using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Jev.Typesafe.Sdk.Serialization;

internal static class ResponseDecoder
{
    internal static JsonSerializerOptions CreateOptions(JsonSerializerOptions? source = null)
    {
        var options = source is null ? new JsonSerializerOptions(JsonSerializerDefaults.Web) : new JsonSerializerOptions(source);
        options.NumberHandling = JsonNumberHandling.Strict;
        options.Converters.Add(new TypeSafeAnswerConverter());
        options.Converters.Add(new TypeSafeQuestionConverter());
        options.TypeInfoResolver ??= new DefaultJsonTypeInfoResolver();
        return options;
    }

    internal static T Decode<T>(RawHttpResponse raw, JsonSerializerOptions? serializerOptions = null,
        JsonTypeInfo<T>? typeInfo = null, Action<string>? warning = null)
    {
        if (raw is null) throw new ArgumentNullException(nameof(raw));
        if (!raw.IsSuccessStatusCode) throw TypeSafeApiException.FromResponse(raw);
        var unmodeled = new Dictionary<string, JsonElement>();
        var content = raw.Content;
        var options = typeInfo?.Options ?? CreateOptions(serializerOptions);
        try
        {
            using var document = JsonDocument.Parse(raw.Content);
            var root = document.RootElement;
            if (typeof(SystemOneResponse).IsAssignableFrom(typeof(T)))
            {
                RequireKind(root, JsonValueKind.Object, raw, "");
                if (root.TryGetProperty("answers", out var precheck) && precheck.ValueKind == JsonValueKind.Object)
                {
                    foreach (var answer in precheck.EnumerateObject())
                    {
                        var tag = Require(answer.Value, "type", JsonValueKind.String, raw, "answers." + answer.Name);
                        if (tag.GetString() is not ("noul" or "choice" or "score"))
                        {
                            unmodeled[answer.Name] = answer.Value.Clone();
                            warning?.Invoke($"Ignoring answer {PythonRepresentation.Quote(answer.Name)} with unrecognized type {PythonRepresentation.Quote(tag.GetString()!)}");
                        }
                    }
                }
                Require(root, "model", JsonValueKind.String, raw, "");
                ValidateUsage(Require(root, "usage", JsonValueKind.Object, raw, ""), raw);
                if (root.TryGetProperty("answers", out var answers))
                {
                    RequireKind(answers, JsonValueKind.Object, raw, "answers");
                    foreach (var answer in answers.EnumerateObject())
                        if (!unmodeled.ContainsKey(answer.Name)) ValidateAnswer(answer.Value, "answers." + answer.Name, raw);
                }
                var mutable = JsonNode.Parse(raw.Content)!.AsObject();
                if (mutable["answers"] is JsonObject answerMap)
                {
                    foreach (var name in unmodeled.Keys) answerMap.Remove(name);
                    foreach (var property in typeof(T).GetProperties().Where(property => property.DeclaringType != typeof(SystemOneResponse)
                        && typeof(TypeSafeAnswer).IsAssignableFrom(property.PropertyType)))
                    {
                        var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                            ?? options.PropertyNamingPolicy?.ConvertName(property.Name) ?? property.Name;
                        if (answerMap.TryGetPropertyValue(name, out var value)) mutable[name] = value?.DeepClone();
                    }
                }
                content = mutable.ToJsonString();
            }
            else if (typeof(ModelMetadataList).IsAssignableFrom(typeof(T)))
            {
                var models = Require(root, "models", JsonValueKind.Array, raw, "");
                var index = 0;
                foreach (var model in models.EnumerateArray())
                {
                    var path = $"models[{index++}]";
                    Require(model, "name", JsonValueKind.String, raw, path);
                    Require(model, "description", JsonValueKind.String, raw, path);
                    Require(model, "release_date", JsonValueKind.String, raw, path);
                }
            }
            using var prepared = JsonDocument.Parse(content);
            CheckRequired(prepared.RootElement, typeInfo ?? options.GetTypeInfo(typeof(T)), raw, "", options);
            var result = typeInfo is null ? JsonSerializer.Deserialize<T>(content, options) : JsonSerializer.Deserialize(content, typeInfo);
            if (result is null) throw new TypeSafeApiResponseValidationException(raw, "");
            if (result is TypeSafeResponse response) response.Attach(raw);
            if (result is SystemOneResponse systemOne) systemOne.UnmodeledAnswers = unmodeled;
            return result;
        }
        catch (JsonException error)
        {
            throw new TypeSafeApiResponseValidationException(raw, (error.Path ?? "").TrimStart('$', '.'));
        }
    }

    private static void CheckRequired(JsonElement value, JsonTypeInfo typeInfo, RawHttpResponse raw, string path, JsonSerializerOptions options)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object || value.ValueKind != JsonValueKind.Object) return;
        foreach (var property in typeInfo.Properties)
        {
            var propertyPath = path.Length == 0 ? property.Name : path + "." + property.Name;
            var found = value.TryGetProperty(property.Name, out var child);
            if (!found && options.PropertyNameCaseInsensitive)
            {
                foreach (var pair in value.EnumerateObject())
                    if (string.Equals(pair.Name, property.Name, StringComparison.OrdinalIgnoreCase)) { child = pair.Value; found = true; break; }
            }
            if (!found && property.IsRequired) throw new TypeSafeApiResponseValidationException(raw, propertyPath);
            if (found && child.ValueKind != JsonValueKind.Null)
            {
                if (typeof(TypeSafeAnswer).IsAssignableFrom(property.PropertyType)) ValidateAnswer(child, propertyPath, raw);
                else CheckRequired(child, options.GetTypeInfo(property.PropertyType), raw, propertyPath, options);
            }
        }
    }

    private static JsonElement Require(JsonElement root, string name, JsonValueKind kind, RawHttpResponse raw, string path)
    {
        var fieldPath = path.Length == 0 ? name : path + "." + name;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(name, out var value) || value.ValueKind != kind)
            throw new TypeSafeApiResponseValidationException(raw, fieldPath);
        return value;
    }

    private static void RequireKind(JsonElement value, JsonValueKind kind, RawHttpResponse raw, string path)
    {
        if (value.ValueKind != kind) throw new TypeSafeApiResponseValidationException(raw, path);
    }

    private static void ValidateUsage(JsonElement usage, RawHttpResponse raw)
    {
        foreach (var name in new[] { "input_tokens", "output_tokens" })
            if (usage.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
                && (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out _)))
                throw new TypeSafeApiResponseValidationException(raw, "usage." + name);
    }

    private static void ValidateAnswer(JsonElement answer, string path, RawHttpResponse raw)
    {
        var type = Require(answer, "type", JsonValueKind.String, raw, path).GetString();
        Require(answer, type!, type == "choice" ? JsonValueKind.String : JsonValueKind.Number, raw, path);
        if (type == "noul") return;
        Require(answer, "confidence", JsonValueKind.Number, raw, path);
        if (type == "score")
        {
            var legend = Require(answer, "legend", JsonValueKind.Object, raw, path);
            foreach (var entry in legend.EnumerateObject())
                if (!int.TryParse(entry.Name, out _) || entry.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Object or JsonValueKind.Array))
                    throw new TypeSafeApiResponseValidationException(raw, path + ".legend." + entry.Name);
        }
        var probabilities = Require(answer, "probabilities", JsonValueKind.Object, raw, path);
        foreach (var entry in probabilities.EnumerateObject())
            if (entry.Value.ValueKind != JsonValueKind.Number || type == "score" && !int.TryParse(entry.Name, out _))
                throw new TypeSafeApiResponseValidationException(raw, path + ".probabilities." + entry.Name);
    }
}