namespace PolymorphicOwned.Sample;

/// <summary>
/// A discount applied to an order. It is a value object with no identity of its own — exactly the
/// shape EF Core cannot map polymorphically without this library.
/// </summary>
public interface IDiscount
{
}

/// <summary>Take a percentage off, capped at a maximum amount, once a minimum item count is met.</summary>
public sealed class PercentageDiscount : IDiscount
{
    public double Percentage { get; set; }

    public double MaxAmount { get; set; }

    public int MinItems { get; set; }
}

/// <summary>Take a fixed amount off once the order total qualifies, up to a redemption limit.</summary>
public sealed class FixedAmountDiscount : IDiscount
{
    public double Amount { get; set; }

    public double MinOrderTotal { get; set; }

    public int MaxRedemptions { get; set; }
}
