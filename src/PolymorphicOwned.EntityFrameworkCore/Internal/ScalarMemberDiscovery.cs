using System.Reflection;

namespace PolymorphicOwned.EntityFrameworkCore.Internal;

/// <summary>
/// Finds the scalar members of a concrete subtype that should be flattened into columns.
/// Mirrors EF Core's own notion of a "scalar" so the columns we register line up with the
/// types EF would have mapped had the member lived directly on the entity.
/// </summary>
internal static class ScalarMemberDiscovery
{
    public static IReadOnlyList<PropertyInfo> Discover(Type subtype)
    {
        // Include inherited members so an abstract-base member is seen on every subtype
        // (and therefore treated as shared). Interfaces contribute nothing concrete, so the
        // subtype's own + inherited class members are exactly the flattenable surface.
        return subtype
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .Where(p => IsScalar(p.PropertyType))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .ToArray();
    }

    public static bool IsScalar(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;

        if (t.IsEnum || t.IsPrimitive)
        {
            return true;
        }

        return t == typeof(string)
            || t == typeof(decimal)
            || t == typeof(DateTime)
            || t == typeof(DateTimeOffset)
            || t == typeof(DateOnly)
            || t == typeof(TimeOnly)
            || t == typeof(TimeSpan)
            || t == typeof(Guid)
            || t == typeof(byte[]);
    }

    /// <summary>
    /// The type used for the shadow property backing a member: a nullable form so the column can
    /// be NULL whenever a different subtype is active. Reference types are already nullable.
    /// </summary>
    public static Type ToNullableShadowType(Type memberType)
    {
        if (!memberType.IsValueType)
        {
            return memberType;
        }

        return Nullable.GetUnderlyingType(memberType) is not null
            ? memberType
            : typeof(Nullable<>).MakeGenericType(memberType);
    }
}
