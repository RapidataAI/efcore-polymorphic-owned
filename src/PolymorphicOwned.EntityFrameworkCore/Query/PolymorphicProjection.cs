using System.Collections.Concurrent;
using PolymorphicOwned.EntityFrameworkCore.Metadata;

namespace PolymorphicOwned.EntityFrameworkCore.Query;

/// <summary>
/// Client-side reconstruction target for projected polymorphic values. The query interceptor
/// rewrites a projected polymorphic property (e.g. <c>o.Discount</c>) into a call to
/// <see cref="Rebuild"/> whose arguments are the discriminator + flattened columns selected from the
/// row, so the reconstruction logic lives here in the library rather than in each consumer's projection.
/// </summary>
public static class PolymorphicProjection
{
    private static readonly ConcurrentDictionary<string, Entry> Registry = new();

    /// <summary>Registers a mapping for projection and returns the stable id used in the rewrite.</summary>
    internal static string Register(PolymorphicMapping mapping)
    {
        var id = $"{mapping.OwnerClrType.FullName}|{mapping.NavigationName}";
        Registry[id] = new Entry(mapping, mapping.AllShadowPropertyNames.ToArray());
        return id;
    }

    /// <summary>
    /// Reconstructs the concrete value object from the projected discriminator + column values.
    /// Public because EF's compiled projection shaper invokes it directly; not meant to be called
    /// by hand. <paramref name="values"/> are ordered to match the mapping's shadow properties.
    /// </summary>
    public static object? Rebuild(string mappingId, string? discriminator, object?[] values)
    {
        var entry = Registry[mappingId];
        var subtype = entry.Mapping.ResolveByDiscriminator(discriminator);
        if (subtype is null)
        {
            return null;
        }

        return subtype.Materialize(shadowName =>
        {
            var index = Array.IndexOf(entry.Order, shadowName);
            return index >= 0 ? values[index] : null;
        });
    }

    private readonly record struct Entry(PolymorphicMapping Mapping, string[] Order);
}
