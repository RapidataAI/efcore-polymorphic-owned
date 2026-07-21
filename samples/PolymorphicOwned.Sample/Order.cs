namespace PolymorphicOwned.Sample;

public sealed class Order
{
    public int Id { get; set; }

    public string Reference { get; set; } = string.Empty;

    /// <summary>
    /// The polymorphic owned value object. Stored inline on the orders table as a discriminator
    /// column plus the union of both discount shapes' columns — never a separate table or JSON blob.
    /// </summary>
    public IDiscount Discount { get; set; } = default!;
}
