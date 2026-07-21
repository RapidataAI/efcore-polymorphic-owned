using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace PolymorphicOwned.EntityFrameworkCore.Metadata;

/// <summary>
/// The runtime mappings for a model, keyed by owner CLR type. Rebuilt once per model from the
/// serialized annotation and cached weakly so it lives exactly as long as the (cached) model does.
/// </summary>
internal sealed class PolymorphicModelInfo
{
    private static readonly ConditionalWeakTable<IReadOnlyModel, PolymorphicModelInfo> Cache = new();

    private readonly Dictionary<Type, List<PolymorphicMapping>> _byOwner;

    private PolymorphicModelInfo(Dictionary<Type, List<PolymorphicMapping>> byOwner)
    {
        _byOwner = byOwner;
    }

    public bool IsEmpty => _byOwner.Count == 0;

    public static PolymorphicModelInfo For(IReadOnlyModel model) =>
        Cache.GetValue(model, Build);

    public IReadOnlyList<PolymorphicMapping>? MappingsFor(Type ownerClrType) =>
        _byOwner.TryGetValue(ownerClrType, out var mappings) ? mappings : null;

    private static PolymorphicModelInfo Build(IReadOnlyModel model)
    {
        var byOwner = new Dictionary<Type, List<PolymorphicMapping>>();

        if (model.FindAnnotation(PolymorphicAnnotations.Mappings)?.Value is string json)
        {
            foreach (var descriptor in PolymorphicModelDescriptor.Deserialize(json).Mappings)
            {
                var mapping = descriptor.ToRuntime();
                if (!byOwner.TryGetValue(mapping.OwnerClrType, out var list))
                {
                    list = new List<PolymorphicMapping>();
                    byOwner.Add(mapping.OwnerClrType, list);
                }

                list.Add(mapping);
            }
        }

        return new PolymorphicModelInfo(byOwner);
    }
}
