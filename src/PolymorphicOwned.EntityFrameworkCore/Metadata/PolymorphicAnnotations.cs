namespace PolymorphicOwned.EntityFrameworkCore.Metadata;

internal static class PolymorphicAnnotations
{
    /// <summary>
    /// Model-level annotation holding the serialized mapping table. Stored as a JSON string so the
    /// migrations snapshot generator can render it as a plain literal; the reflection-heavy runtime
    /// mapping is rebuilt from it on demand and cached per model.
    /// </summary>
    public const string Mappings = "PolymorphicOwned:Mappings";
}
