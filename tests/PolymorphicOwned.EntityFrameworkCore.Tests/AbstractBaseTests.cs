using Microsoft.EntityFrameworkCore;
using PolymorphicOwned.EntityFrameworkCore.Tests.Infrastructure;
using PolymorphicOwned.EntityFrameworkCore.Tests.Model;
using Shouldly;
using Xunit;

namespace PolymorphicOwned.EntityFrameworkCore.Tests;

/// <summary>
/// The base value object may be an interface (covered elsewhere) or an <b>abstract class</b>. This
/// covers the abstract-class path end to end on both backends: a record-like immutable subtype
/// (constructor activation) and a mutable subtype (setter activation) round-trip as concrete types.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AbstractBaseTests(PostgresFixture postgres) : DatabaseTestBase(postgres)
{
    [Fact]
    public async Task Reads_back_each_subtype_of_an_abstract_base_as_its_concrete_type()
    {
        foreach (var backend in Backends<ShipmentContext>())
        {
            int flatId, weightId;
            await using (var ctx = backend.NewContext())
            {
                await ctx.Database.EnsureCreatedAsync();

                var flat = new Shipment { Rate = new FlatRate(4.99, 3) };
                var weight = new Shipment { Rate = new WeightBasedRate { PerKilo = 1.5, MinimumCharge = 2.0 } };
                ctx.Shipments.AddRange(flat, weight);
                await ctx.SaveChangesAsync();
                (flatId, weightId) = (flat.Id, weight.Id);
            }

            await using (var read = backend.NewContext())
            {
                var flat = await read.Shipments.SingleAsync(s => s.Id == flatId);
                var flatRate = flat.Rate.ShouldBeOfType<FlatRate>();
                flatRate.Amount.ShouldBe(4.99, backend.Name);
                flatRate.FreeOverItems.ShouldBe(3, backend.Name);

                var weight = await read.Shipments.SingleAsync(s => s.Id == weightId);
                var weightRate = weight.Rate.ShouldBeOfType<WeightBasedRate>();
                weightRate.PerKilo.ShouldBe(1.5, backend.Name);
                weightRate.MinimumCharge.ShouldBe(2.0, backend.Name);

                // The inactive subtype's column is NULL — filter server-side on the real column.
                var cheapFlat = await read.Shipments
                    .Where(s => EF.Property<double?>(s, "Amount") < 10)
                    .Select(s => s.Id)
                    .ToListAsync();
                cheapFlat.ShouldBe(new[] { flatId });
            }
        }
    }
}
