using System.Reflection;

namespace PolymorphicOwned.EntityFrameworkCore.Metadata;

/// <summary>
/// The complete runtime picture of one <c>OwnsPolymorphic</c> mapping: which owner property holds
/// the value object, the discriminator shadow property, and the subtypes with their contributed
/// shadow columns. Interceptors read this to flatten (save) and reconstruct (materialize).
/// </summary>
internal sealed class PolymorphicMapping
{
    private readonly PropertyInfo _navigation;
    private readonly Dictionary<Type, SubtypeMapping> _byClrType;
    private readonly Dictionary<string, SubtypeMapping> _byDiscriminator;

    public PolymorphicMapping(
        Type ownerClrType,
        PropertyInfo navigation,
        bool isRequired,
        string discriminatorPropertyName,
        IReadOnlyList<SubtypeMapping> subtypes)
    {
        OwnerClrType = ownerClrType;
        _navigation = navigation;
        IsRequired = isRequired;
        DiscriminatorPropertyName = discriminatorPropertyName;
        Subtypes = subtypes;
        _byClrType = subtypes.ToDictionary(s => s.ClrType);
        _byDiscriminator = subtypes.ToDictionary(s => s.DiscriminatorValue, StringComparer.Ordinal);
        AllShadowPropertyNames = subtypes
            .SelectMany(s => s.ShadowToMember.Keys)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    public Type OwnerClrType { get; }

    public string NavigationName => _navigation.Name;

    public bool IsRequired { get; }

    public string DiscriminatorPropertyName { get; }

    public IReadOnlyList<SubtypeMapping> Subtypes { get; }

    /// <summary>Union of every subtype's shadow property names — the columns to null when inactive.</summary>
    public IReadOnlyList<string> AllShadowPropertyNames { get; }

    public object? ReadValue(object owner) => _navigation.GetValue(owner);

    public void WriteValue(object owner, object? value) => _navigation.SetValue(owner, value);

    public SubtypeMapping ResolveByInstance(object value)
    {
        var type = value.GetType();
        if (_byClrType.TryGetValue(type, out var exact))
        {
            return exact;
        }

        // Tolerate proxies / further-derived instances by taking the most-derived registered base.
        SubtypeMapping? best = null;
        foreach (var subtype in Subtypes)
        {
            if (subtype.ClrType.IsAssignableFrom(type) &&
                (best is null || best.ClrType.IsAssignableFrom(subtype.ClrType)))
            {
                best = subtype;
            }
        }

        return best ?? throw new InvalidOperationException(
            $"Value of type '{type}' is not a registered subtype of polymorphic property " +
            $"'{OwnerClrType.Name}.{NavigationName}'. Register it with HasDerivedType<{type.Name}>(...).");
    }

    public SubtypeMapping? ResolveByDiscriminator(string? discriminator)
    {
        if (discriminator is null)
        {
            return null;
        }

        return _byDiscriminator.TryGetValue(discriminator, out var subtype) ? subtype : null;
    }
}
