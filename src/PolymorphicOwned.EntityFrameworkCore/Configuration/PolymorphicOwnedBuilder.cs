using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolymorphicOwned.EntityFrameworkCore.Internal;
using PolymorphicOwned.EntityFrameworkCore.Metadata;

namespace PolymorphicOwned.EntityFrameworkCore.Configuration;

/// <summary>
/// Fluent configuration for a polymorphic owned value object mapped inline onto the owner's table.
/// </summary>
/// <typeparam name="TValue">The interface or abstract base type of the value object.</typeparam>
public sealed class PolymorphicOwnedBuilder<TValue>
    where TValue : class
{
    private readonly List<(Type Type, string Discriminator)> _subtypes = new();
    private readonly List<MemberOverride> _overrides = new();

    private string? _discriminatorPropertyName;
    private string? _discriminatorColumnName;
    private bool _isRequired = true;
    private bool _collapseSharedMembers;

    internal PolymorphicOwnedBuilder(PropertyInfo navigation)
    {
        Navigation = navigation;
    }

    internal PropertyInfo Navigation { get; }

    /// <summary>
    /// Sets the discriminator column/property name. Defaults to <c>&lt;property&gt;_type</c>
    /// (e.g. <c>GraduationRule_type</c>, snake-cased to <c>graduation_rule_type</c> when a
    /// naming-convention plugin is active).
    /// </summary>
    public PolymorphicOwnedBuilder<TValue> HasDiscriminatorColumn(string name)
    {
        _discriminatorPropertyName = name;
        _discriminatorColumnName = name;
        return this;
    }

    /// <summary>Registers a concrete subtype and the value stored in the discriminator column.</summary>
    public PolymorphicOwnedBuilder<TValue> HasDerivedType<TDerived>(string discriminatorValue)
        where TDerived : class, TValue
    {
        if (_subtypes.Any(s => s.Type == typeof(TDerived)))
        {
            throw new InvalidOperationException($"Subtype '{typeof(TDerived)}' is already registered.");
        }

        if (_subtypes.Any(s => string.Equals(s.Discriminator, discriminatorValue, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Discriminator value '{discriminatorValue}' is already used by another subtype.");
        }

        _subtypes.Add((typeof(TDerived), discriminatorValue));
        return this;
    }

    /// <summary>
    /// Configures the column for a specific subtype member, e.g.
    /// <c>poly.Property(r =&gt; ((ScoreThresholdRule)r).GraduationScore).HasColumnName("graduation_score")</c>.
    /// </summary>
    public PolymorphicMemberBuilder Property(Expression<Func<TValue, object?>> memberSelector)
    {
        var (subtype, memberName) = ResolveMember(memberSelector);
        var memberOverride = new MemberOverride { Subtype = subtype, MemberName = memberName };
        _overrides.Add(memberOverride);
        return new PolymorphicMemberBuilder(memberOverride);
    }

    /// <summary>Marks the owner's value object as required (the default) or optional.</summary>
    public PolymorphicOwnedBuilder<TValue> IsRequired(bool required = true)
    {
        _isRequired = required;
        return this;
    }

    /// <summary>Marks the owner's value object as optional (the discriminator column becomes nullable).</summary>
    public PolymorphicOwnedBuilder<TValue> IsOptional() => IsRequired(false);

    /// <summary>
    /// Opts into collapsing members that share the same name and type across subtypes onto a single
    /// column. Without this, each subtype's members get their own (subtype-qualified when colliding)
    /// columns.
    /// </summary>
    public PolymorphicOwnedBuilder<TValue> CollapseSharedMembers()
    {
        _collapseSharedMembers = true;
        return this;
    }

    /// <summary>
    /// Registers the shadow properties (discriminator + union of subtype scalars) on the owner and
    /// records the serialized mapping onto the model. Runs after the user's configuration lambda.
    /// </summary>
    internal void Apply(EntityTypeBuilder ownerBuilder)
    {
        if (_subtypes.Count == 0)
        {
            throw new InvalidOperationException(
                $"OwnsPolymorphic on '{ownerBuilder.Metadata.ClrType.Name}.{Navigation.Name}' " +
                "requires at least one HasDerivedType<T>(...) call.");
        }

        // The CLR property holds an interface/abstract instance EF cannot map; we own it entirely.
        ownerBuilder.Ignore(Navigation.Name);

        var plan = BuildPlan();

        var discriminatorProperty = ownerBuilder.Property<string>(plan.DiscriminatorPropertyName);
        discriminatorProperty.IsRequired(_isRequired);
        if (_discriminatorColumnName is not null)
        {
            discriminatorProperty.HasColumnName(_discriminatorColumnName);
        }

        foreach (var shadow in plan.ShadowProperties)
        {
            // Shadow types are only known at runtime, so the non-generic overload is required here.
#pragma warning disable CA2263
            var property = ownerBuilder.Property(shadow.ClrType, shadow.Name);
#pragma warning restore CA2263
            property.IsRequired(false); // subtype-specific members are always nullable columns
            if (shadow.ColumnName is not null)
            {
                property.HasColumnName(shadow.ColumnName);
            }
        }

        WriteDescriptor(ownerBuilder, plan);
    }

    private MappingPlan BuildPlan()
    {
        var discriminatorPropertyName = _discriminatorPropertyName ?? $"{Navigation.Name}_type";

        var discovered = _subtypes
            .Select(s => (s.Type, s.Discriminator, Members: ScalarMemberDiscovery.Discover(s.Type)))
            .ToList();

        var nameOccurrences = discovered
            .SelectMany(s => s.Members)
            .GroupBy(m => m.Name, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(m => m.PropertyType).Distinct().ToArray(),
                StringComparer.Ordinal);

        var shadowByName = new Dictionary<string, ShadowProperty>(StringComparer.Ordinal);
        var subtypeDescriptors = new List<SubtypeDescriptor>();

        foreach (var subtype in discovered)
        {
            var memberDescriptors = new List<MemberDescriptor>();

            foreach (var member in subtype.Members)
            {
                var typesForName = nameOccurrences[member.Name];
                var shared = typesForName.Length == 1 && CountSubtypesWith(discovered, member.Name) > 1;

                var shadowName = (_collapseSharedMembers && shared)
                    ? member.Name
                    : (CountSubtypesWith(discovered, member.Name) > 1
                        ? $"{subtype.Type.Name}_{member.Name}"
                        : member.Name);

                var columnOverride = _overrides.FirstOrDefault(o =>
                    o.Subtype == subtype.Type &&
                    string.Equals(o.MemberName, member.Name, StringComparison.Ordinal))?.ColumnName;

                if (!shadowByName.TryGetValue(shadowName, out var existing))
                {
                    shadowByName[shadowName] = new ShadowProperty(
                        shadowName,
                        ScalarMemberDiscovery.ToNullableShadowType(member.PropertyType),
                        columnOverride);
                }
                else if (columnOverride is not null && existing.ColumnName is null)
                {
                    shadowByName[shadowName] = existing with { ColumnName = columnOverride };
                }

                memberDescriptors.Add(new MemberDescriptor { Member = member.Name, Shadow = shadowName });
            }

            subtypeDescriptors.Add(new SubtypeDescriptor
            {
                ClrType = AssemblyQualifiedName(subtype.Type),
                Discriminator = subtype.Discriminator,
                Members = memberDescriptors,
            });
        }

        return new MappingPlan(discriminatorPropertyName, shadowByName.Values.ToArray(), subtypeDescriptors);
    }

    private void WriteDescriptor(EntityTypeBuilder ownerBuilder, MappingPlan plan)
    {
        var model = ownerBuilder.Metadata.Model;
        var existingJson = model.FindAnnotation(PolymorphicAnnotations.Mappings)?.Value as string;
        var descriptor = existingJson is null
            ? new PolymorphicModelDescriptor()
            : PolymorphicModelDescriptor.Deserialize(existingJson);

        descriptor.Mappings.Add(new PolymorphicMappingDescriptor
        {
            OwnerType = AssemblyQualifiedName(ownerBuilder.Metadata.ClrType),
            Navigation = Navigation.Name,
            Required = _isRequired,
            DiscriminatorProperty = plan.DiscriminatorPropertyName,
            Subtypes = plan.Subtypes,
        });

        model.SetAnnotation(PolymorphicAnnotations.Mappings, descriptor.Serialize());
    }

    private static int CountSubtypesWith(
        List<(Type Type, string Discriminator, IReadOnlyList<PropertyInfo> Members)> discovered,
        string memberName) =>
        discovered.Count(s => s.Members.Any(m => string.Equals(m.Name, memberName, StringComparison.Ordinal)));

    private static (Type Subtype, string MemberName) ResolveMember(Expression<Func<TValue, object?>> selector)
    {
        var body = selector.Body;
        while (body is UnaryExpression { NodeType: ExpressionType.Convert } unary)
        {
            body = unary.Operand;
        }

        if (body is not MemberExpression memberExpression)
        {
            throw new ArgumentException(
                "Expression must select a subtype member, e.g. r => ((ScoreThresholdRule)r).GraduationScore.",
                nameof(selector));
        }

        // The cast in ((Subtype)r).Member tells us which subtype the override targets.
        var subtype = memberExpression.Expression?.Type ?? memberExpression.Member.DeclaringType!;
        return (subtype, memberExpression.Member.Name);
    }

    private static string AssemblyQualifiedName(Type type) =>
        type.AssemblyQualifiedName
        ?? throw new InvalidOperationException($"Type '{type}' has no assembly-qualified name.");

    private sealed record ShadowProperty(string Name, Type ClrType, string? ColumnName);

    private sealed record MappingPlan(
        string DiscriminatorPropertyName,
        IReadOnlyList<ShadowProperty> ShadowProperties,
        List<SubtypeDescriptor> Subtypes);
}
