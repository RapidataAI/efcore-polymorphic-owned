using System.Runtime.CompilerServices;

namespace PolymorphicOwned.EntityFrameworkCore.Internal;

internal static class Activation
{
    /// <summary>
    /// Creates an instance without running a constructor, used only when a subtype exposes no
    /// usable constructor. Members are assigned afterwards (via setters or backing fields).
    /// </summary>
    public static object CreateUninitialized(Type type) => RuntimeHelpers.GetUninitializedObject(type);
}
