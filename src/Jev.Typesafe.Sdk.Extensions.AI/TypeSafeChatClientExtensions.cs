using Microsoft.Extensions.AI;

namespace Jev.Typesafe.Sdk.Extensions.AI;

public static class TypeSafeChatClientExtensions
{
    public static IChatClient AsChatClient(
        this TypeSafeClient client,
        IReadOnlyDictionary<string, TypeSafeQuestion> questions,
        bool ownsClient = false
    ) => new TypeSafeChatClient(client, questions, ownsClient);
}