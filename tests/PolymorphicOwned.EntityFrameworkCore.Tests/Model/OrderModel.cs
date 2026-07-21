using Microsoft.EntityFrameworkCore;
using PolymorphicOwned.EntityFrameworkCore;

namespace PolymorphicOwned.EntityFrameworkCore.Tests.Model;

public interface IDiscount;

public sealed class PercentageDiscount : IDiscount
{
    public double Percentage { get; set; }

    public double MaxAmount { get; set; }

    public int MinItems { get; set; }
}

public sealed class FixedAmountDiscount : IDiscount
{
    public double Amount { get; set; }

    public double MinOrderTotal { get; set; }

    public int MaxRedemptions { get; set; }
}

public sealed class Order
{
    public int Id { get; set; }

    public string Reference { get; set; } = string.Empty;

    public IDiscount Discount { get; set; } = default!;
}

/// <summary>
/// The motivating example, with the snake_case naming convention enabled — used to assert the exact
/// migration columns and that naming-convention plugins rewrite the shadow columns.
/// </summary>
public sealed class OrderContext(DbContextOptions<OrderContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(order =>
        {
            order.HasKey(o => o.Id);
            order.OwnsPolymorphic(o => o.Discount, poly =>
            {
                poly.HasDiscriminatorColumn("discount_type");
                poly.HasDerivedType<PercentageDiscount>("percentage");
                poly.HasDerivedType<FixedAmountDiscount>("fixed_amount");
            });
        });
    }
}
