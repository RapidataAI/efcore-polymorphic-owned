using Microsoft.EntityFrameworkCore;
using PolymorphicOwned.EntityFrameworkCore;

namespace PolymorphicOwned.EntityFrameworkCore.Tests.Model;

/// <summary>Abstract-class base (not an interface) — proves that path is supported end to end.</summary>
public abstract class ShippingRate;

/// <summary>
/// Immutable subtype with get-only properties and a matching constructor — exercises
/// constructor-based activation over an abstract base.
/// </summary>
public sealed class FlatRate(double amount, int freeOverItems) : ShippingRate
{
    public double Amount { get; } = amount;

    public int FreeOverItems { get; } = freeOverItems;
}

/// <summary>Mutable subtype — exercises setter-based activation over an abstract base.</summary>
public sealed class WeightBasedRate : ShippingRate
{
    public double PerKilo { get; set; }

    public double MinimumCharge { get; set; }
}

public sealed class Shipment
{
    public int Id { get; set; }

    public ShippingRate Rate { get; set; } = default!;
}

/// <summary>Two subtypes over an <b>abstract class</b> base, one immutable and one mutable.</summary>
public sealed class ShipmentContext(DbContextOptions<ShipmentContext> options) : DbContext(options)
{
    public DbSet<Shipment> Shipments => Set<Shipment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Shipment>(shipment =>
        {
            shipment.HasKey(s => s.Id);
            shipment.OwnsPolymorphic(s => s.Rate, poly =>
            {
                poly.HasDerivedType<FlatRate>("flat");
                poly.HasDerivedType<WeightBasedRate>("weight_based");
            });
        });
    }
}
