namespace PolymorphicOwned.Sample;

public sealed class Audience
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The polymorphic owned value object. Stored inline on the audiences table as a discriminator
    /// column plus the union of both rule shapes' columns — never a separate table or JSON blob.
    /// </summary>
    public GraduationRule GraduationRule { get; set; } = default!;
}
