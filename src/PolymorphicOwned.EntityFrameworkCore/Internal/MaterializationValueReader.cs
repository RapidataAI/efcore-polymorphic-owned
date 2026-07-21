using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PolymorphicOwned.EntityFrameworkCore.Internal;

/// <summary>
/// Reads shadow-property values out of <see cref="MaterializationInterceptionData"/>. Its
/// <c>GetPropertyValue&lt;T&gt;</c> builds a typed accessor internally and casts on the exact
/// property CLR type, so it must be invoked with that closed type rather than <c>object</c>.
/// </summary>
internal static class MaterializationValueReader
{
    private static readonly MethodInfo OpenGenericGetPropertyValue = typeof(MaterializationInterceptionData)
        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .Single(m => m.Name == "GetPropertyValue"
            && m.IsGenericMethodDefinition
            && m.GetParameters() is [{ ParameterType.Name: "String" }]);

    private static readonly ConcurrentDictionary<Type, MethodInfo> ClosedByType = new();

    public static object? Read(MaterializationInterceptionData data, string propertyName, Type clrType)
    {
        var method = ClosedByType.GetOrAdd(clrType, static t => OpenGenericGetPropertyValue.MakeGenericMethod(t));
        return method.Invoke(data, new object[] { propertyName });
    }
}
