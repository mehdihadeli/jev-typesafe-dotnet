using Microsoft.Extensions.AI;

namespace Jev.Typesafe.Sdk.Extensions.AI;

public sealed class TypeSafeChatOptions : ChatOptions
{
    public object? State { get; set; }

    public IReadOnlyDictionary<string, TypeSafeQuestion>? Questions { get; set; }

    public TypeSafeChatOptions() { }

    private TypeSafeChatOptions(TypeSafeChatOptions other)
        : base(other)
    {
        State = other.State;
        Questions = other.Questions is null
            ? null
            : new System.Collections.ObjectModel.ReadOnlyDictionary<string, TypeSafeQuestion>(
                other.Questions.ToDictionary(pair => pair.Key, pair => pair.Value)
            );
    }

    public override ChatOptions Clone() => new TypeSafeChatOptions(this);
}