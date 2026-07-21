using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PolymorphicOwned.EntityFrameworkCore;

namespace PolymorphicOwned.Sample;

public sealed class OrderDbContext(DbContextOptions<OrderDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(order =>
        {
            order.HasKey(o => o.Id);
            order.Property(o => o.Reference).HasMaxLength(200);

            order.OwnsPolymorphic(o => o.Discount, poly =>
            {
                poly.HasDiscriminatorColumn("discount_type");
                poly.HasDerivedType<PercentageDiscount>("percentage");
                poly.HasDerivedType<FixedAmountDiscount>("fixed_amount");
            });
        });
    }
}

/// <summary>
/// Lets <c>dotnet ef</c> build the context at design time (for <c>migrations add</c>) without a
/// running database. The connection string is only used when the sample actually talks to Postgres.
/// </summary>
public sealed class OrderDbContextFactory : IDesignTimeDbContextFactory<OrderDbContext>
{
    public OrderDbContext CreateDbContext(string[] args) =>
        new(SampleOptions.Build().Options);
}

internal static class SampleOptions
{
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=polymorphic_owned_sample;Username=postgres;Password=postgres";

    public static DbContextOptionsBuilder<OrderDbContext> Build(string? connectionString = null)
    {
        var connection = connectionString
            ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION")
            ?? DefaultConnectionString;

        return new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql(connection)
            .UseSnakeCaseNamingConvention()
            .UsePolymorphicOwned();
    }
}
