using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PolymorphicOwned.EntityFrameworkCore.Metadata;

/// <summary>
/// The serializable, reflection-free description of the mappings. This is what lives in the model
/// annotation (and therefore the migrations snapshot); <see cref="PolymorphicMapping"/> is rebuilt
/// from it at runtime.
/// </summary>
internal sealed class PolymorphicModelDescriptor
{
    [JsonPropertyName("mappings")]
    public List<PolymorphicMappingDescriptor> Mappings { get; set; } = new();

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions);

    public static PolymorphicModelDescriptor Deserialize(string json) =>
        JsonSerializer.Deserialize<PolymorphicModelDescriptor>(json, SerializerOptions)
        ?? new PolymorphicModelDescriptor();
}

internal sealed class PolymorphicMappingDescriptor
{
    [JsonPropertyName("owner")]
    public string OwnerType { get; set; } = string.Empty;

    [JsonPropertyName("nav")]
    public string Navigation { get; set; } = string.Empty;

    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("discriminator")]
    public string DiscriminatorProperty { get; set; } = string.Empty;

    [JsonPropertyName("subtypes")]
    public List<SubtypeDescriptor> Subtypes { get; set; } = new();

    public PolymorphicMapping ToRuntime()
    {
        var ownerType = ResolveType(OwnerType);
        var navigation = ownerType.GetProperty(Navigation)
            ?? throw new InvalidOperationException(
                $"Polymorphic navigation '{Navigation}' not found on '{ownerType}'.");

        var subtypes = Subtypes
            .Select(s => SubtypeMapping.Create(
                ResolveType(s.ClrType),
                s.Discriminator,
                s.Members.ToDictionary(m => m.Member, m => m.Shadow, StringComparer.Ordinal)))
            .ToArray();

        return new PolymorphicMapping(ownerType, navigation, Required, DiscriminatorProperty, subtypes);
    }

    private static Type ResolveType(string assemblyQualifiedName) =>
        Type.GetType(assemblyQualifiedName, throwOnError: true)!;
}

internal sealed class SubtypeDescriptor
{
    [JsonPropertyName("clr")]
    public string ClrType { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Discriminator { get; set; } = string.Empty;

    [JsonPropertyName("members")]
    public List<MemberDescriptor> Members { get; set; } = new();
}

internal sealed class MemberDescriptor
{
    [JsonPropertyName("member")]
    public string Member { get; set; } = string.Empty;

    [JsonPropertyName("shadow")]
    public string Shadow { get; set; } = string.Empty;
}
