using System.Text.Json;

namespace Jev.Typesafe.Sdk.Extensions.AI;

internal static class TypeSafeChatSerialization
{
    internal static string SerializeAnswers(IReadOnlyDictionary<string, TypeSafeAnswer> answers) =>
        JsonSerializer.Serialize(answers.ToDictionary(pair => pair.Key, pair => (object)pair.Value));
}