using Microsoft.EntityFrameworkCore.Diagnostics;
using PolymorphicOwned.EntityFrameworkCore.Internal;
using PolymorphicOwned.EntityFrameworkCore.Metadata;

namespace PolymorphicOwned.EntityFrameworkCore.Interceptors;

/// <summary>
/// After EF materializes an owner entity, reads the discriminator + shadow columns and reconstructs
/// the concrete value object, assigning it to the owner's (otherwise ignored) CLR property.
/// </summary>
public sealed class PolymorphicMaterializationInterceptor : IMaterializationInterceptor
{
    public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
    {
        var entityType = materializationData.EntityType;
        var info = PolymorphicModelInfo.For(entityType.Model);
        if (info.IsEmpty)
        {
            return entity;
        }

        var mappings = info.MappingsFor(entityType.ClrType);
        if (mappings is null)
        {
            return entity;
        }

        foreach (var mapping in mappings)
        {
            var discriminator = ReadShadow(materializationData, entityType, mapping.DiscriminatorPropertyName) as string;
            var subtype = mapping.ResolveByDiscriminator(discriminator);
            if (subtype is null)
            {
                mapping.WriteValue(entity, null);
                continue;
            }

            var value = subtype.Materialize(shadowName => ReadShadow(materializationData, entityType, shadowName));
            mapping.WriteValue(entity, value);
        }

        return entity;
    }

    private static object? ReadShadow(
        MaterializationInterceptionData data,
        Microsoft.EntityFrameworkCore.Metadata.IReadOnlyEntityType entityType,
        string propertyName)
    {
        var property = entityType.FindProperty(propertyName)
            ?? throw new InvalidOperationException(
                $"Shadow property '{propertyName}' is missing from '{entityType.ClrType.Name}'.");
        return MaterializationValueReader.Read(data, propertyName, property.ClrType);
    }
}
