using System.Reflection;
using PolymorphicOwned.EntityFrameworkCore.Internal;

namespace PolymorphicOwned.EntityFrameworkCore.Metadata;

/// <summary>
/// Runtime mapping for a single concrete subtype of a polymorphic owned value object:
/// its discriminator value, the members it contributes, and how to reconstruct an instance.
/// </summary>
internal sealed class SubtypeMapping
{
    private readonly ConstructorInfo? _constructor;
    private readonly MemberBinding[] _constructorParameters;
    private readonly MemberBinding[] _memberSetters;

    private SubtypeMapping(
        Type clrType,
        string discriminatorValue,
        IReadOnlyDictionary<string, MemberBinding> shadowToMember,
        ConstructorInfo? constructor,
        MemberBinding[] constructorParameters,
        MemberBinding[] memberSetters)
    {
        ClrType = clrType;
        DiscriminatorValue = discriminatorValue;
        ShadowToMember = shadowToMember;
        _constructor = constructor;
        _constructorParameters = constructorParameters;
        _memberSetters = memberSetters;
    }

    public Type ClrType { get; }

    public string DiscriminatorValue { get; }

    /// <summary>Shadow-property-name -> the binding that reads its value from an instance.</summary>
    public IReadOnlyDictionary<string, MemberBinding> ShadowToMember { get; }

    /// <summary>The shadow property backing a CLR member of this subtype, or null if not flattened.</summary>
    public string? ShadowNameForMember(string memberName)
    {
        foreach (var (shadowName, binding) in ShadowToMember)
        {
            if (string.Equals(binding.MemberName, memberName, StringComparison.Ordinal))
            {
                return shadowName;
            }
        }

        return null;
    }

    public static SubtypeMapping Create(
        Type clrType,
        string discriminatorValue,
        IReadOnlyDictionary<string, string> memberToShadow)
    {
        var bindingsByMember = memberToShadow.Keys.ToDictionary(
            memberName => memberName,
            memberName => new MemberBinding(clrType.GetProperty(memberName)!),
            StringComparer.Ordinal);

        var shadowToMember = memberToShadow.ToDictionary(
            kvp => kvp.Value,
            kvp => bindingsByMember[kvp.Key],
            StringComparer.Ordinal);

        var (ctor, ctorParams, remaining) = PlanActivation(clrType, bindingsByMember);

        return new SubtypeMapping(clrType, discriminatorValue, shadowToMember, ctor, ctorParams, remaining);
    }

    /// <summary>
    /// Builds a concrete instance from shadow-column values. Prefers a constructor whose parameters
    /// map onto members by name (records / immutable VOs); otherwise activates and assigns members.
    /// </summary>
    public object Materialize(Func<string, object?> shadowValue)
    {
        object instance;
        if (_constructor is not null)
        {
            var args = new object?[_constructorParameters.Length];
            for (var i = 0; i < _constructorParameters.Length; i++)
            {
                var binding = _constructorParameters[i];
                args[i] = ValueCoercion.Coerce(shadowValue(ShadowNameFor(binding)), binding.MemberType);
            }

            instance = _constructor.Invoke(args);
        }
        else
        {
            instance = Activation.CreateUninitialized(ClrType);
        }

        foreach (var binding in _memberSetters)
        {
            binding.Write(instance, shadowValue(ShadowNameFor(binding)));
        }

        return instance;
    }

    private string ShadowNameFor(MemberBinding binding)
    {
        foreach (var kvp in ShadowToMember)
        {
            if (ReferenceEquals(kvp.Value, binding))
            {
                return kvp.Key;
            }
        }

        // Unreachable: every binding originates from ShadowToMember.
        throw new InvalidOperationException($"No shadow property for member '{binding.MemberName}'.");
    }

    private static (ConstructorInfo?, MemberBinding[], MemberBinding[]) PlanActivation(
        Type clrType,
        IReadOnlyDictionary<string, MemberBinding> bindingsByMember)
    {
        var writable = bindingsByMember.Values.Where(b => b.CanWrite).ToArray();

        // A parameterless constructor + writable members is the simplest, most predictable path.
        var parameterless = clrType.GetConstructor(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            Type.EmptyTypes,
            modifiers: null);

        if (parameterless is not null && writable.Length == bindingsByMember.Count)
        {
            return (null, Array.Empty<MemberBinding>(), writable);
        }

        // Otherwise pick the greediest constructor whose parameters all match a member by name.
        var candidate = clrType
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault(c => c.GetParameters().All(p =>
                p.Name is not null && FindBinding(bindingsByMember, p.Name) is not null));

        if (candidate is not null)
        {
            var ctorParams = candidate.GetParameters()
                .Select(p => FindBinding(bindingsByMember, p.Name!)!)
                .ToArray();

            var covered = ctorParams.Select(b => b.MemberName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var remaining = writable.Where(b => !covered.Contains(b.MemberName)).ToArray();
            return (candidate, ctorParams, remaining);
        }

        if (parameterless is not null)
        {
            return (null, Array.Empty<MemberBinding>(), writable);
        }

        // No usable constructor: uninitialized object + backing-field writes.
        return (null, Array.Empty<MemberBinding>(), writable);
    }

    private static MemberBinding? FindBinding(IReadOnlyDictionary<string, MemberBinding> bindings, string name)
    {
        foreach (var kvp in bindings)
        {
            if (string.Equals(kvp.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Value;
            }
        }

        return null;
    }
}
