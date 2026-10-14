#if NETSTANDARD2_0
namespace System.Runtime.CompilerServices
{

internal static class IsExternalInit { }

[AttributeUsage(
    AttributeTargets.Class
        | AttributeTargets.Struct
        | AttributeTargets.Field
        | AttributeTargets.Property,
    Inherited = false
)]
internal sealed class RequiredMemberAttribute : Attribute { }

[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Constructor,
    Inherited = false
)]
internal sealed class CompilerFeatureRequiredAttribute(string feature) : Attribute
{
    public string Feature { get; } = feature;
}
}

namespace System.Diagnostics.CodeAnalysis
{
    [AttributeUsage(AttributeTargets.Constructor, Inherited = false)]
    internal sealed class SetsRequiredMembersAttribute : Attribute { }
}
#endif
