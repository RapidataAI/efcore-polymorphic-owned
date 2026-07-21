using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PolymorphicOwned.EntityFrameworkCore.Metadata;

namespace PolymorphicOwned.EntityFrameworkCore.Interceptors;

/// <summary>
/// Before save, flattens each owner's current value object into its shadow columns: sets the
/// discriminator, populates the active subtype's columns, and nulls the rest. Values are recomputed
/// every save, so an unchanged value object produces no UPDATE while a mutated or swapped one does.
/// </summary>
public sealed class PolymorphicSaveChangesInterceptor : ISaveChangesInterceptor
{
    public InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Flatten(eventData.Context);
        return result;
    }

    public ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Flatten(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void Flatten(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var info = PolymorphicModelInfo.For(context.Model);
        if (info.IsEmpty)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Detached or EntityState.Deleted)
            {
                continue;
            }

            var mappings = info.MappingsFor(entry.Metadata.ClrType);
            if (mappings is null)
            {
                continue;
            }

            foreach (var mapping in mappings)
            {
                FlattenOne(entry, mapping);
            }
        }
    }

    private static void FlattenOne(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry,
        PolymorphicMapping mapping)
    {
        var value = mapping.ReadValue(entry.Entity);

        if (value is null)
        {
            if (mapping.IsRequired)
            {
                throw new InvalidOperationException(
                    $"Polymorphic property '{mapping.OwnerClrType.Name}.{mapping.NavigationName}' is " +
                    "required but was null. Mark it optional with poly.IsOptional() to allow null.");
            }

            entry.Property(mapping.DiscriminatorPropertyName).CurrentValue = null;
            foreach (var shadowName in mapping.AllShadowPropertyNames)
            {
                entry.Property(shadowName).CurrentValue = null;
            }

            return;
        }

        var subtype = mapping.ResolveByInstance(value);
        entry.Property(mapping.DiscriminatorPropertyName).CurrentValue = subtype.DiscriminatorValue;

        foreach (var shadowName in mapping.AllShadowPropertyNames)
        {
            var current = subtype.ShadowToMember.TryGetValue(shadowName, out var binding)
                ? binding.Read(value)
                : null;
            entry.Property(shadowName).CurrentValue = current;
        }
    }
}
