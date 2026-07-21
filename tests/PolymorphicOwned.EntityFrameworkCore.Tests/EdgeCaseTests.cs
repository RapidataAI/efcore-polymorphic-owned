using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using PolymorphicOwned.EntityFrameworkCore.Tests.Infrastructure;
using PolymorphicOwned.EntityFrameworkCore.Tests.Model;
using Shouldly;
using Xunit;

namespace PolymorphicOwned.EntityFrameworkCore.Tests;

[Collection(PostgresCollection.Name)]
public sealed class EdgeCaseTests(PostgresFixture postgres) : DatabaseTestBase(postgres)
{
    [Fact]
    public void Shared_member_maps_to_subtype_qualified_columns_by_default()
    {
        var columns = TableColumns<GadgetContext>();

        columns.ShouldContain("ColorTag_Value");
        columns.ShouldContain("SizeTag_Value");
        columns.ShouldContain("Priority");
        columns.ShouldContain("Scale");
    }

    [Fact]
    public void Shared_member_collapses_to_one_column_when_opted_in()
    {
        var columns = TableColumns<GadgetCollapsedContext>();

        columns.ShouldContain("Value");
        columns.ShouldNotContain("ColorTag_Value");
        columns.ShouldNotContain("SizeTag_Value");
    }

    [Fact]
    public async Task Shared_member_round_trips_for_both_layouts()
    {
        foreach (var backend in Backends<GadgetContext>())
        {
            await RoundTripSharedValue(backend);
        }

        foreach (var backend in Backends<GadgetCollapsedContext>())
        {
            await RoundTripSharedValueCollapsed(backend);
        }
    }

    [Fact]
    public async Task Can_filter_server_side_on_a_flattened_column()
    {
        foreach (var backend in Backends<WidgetContext>())
        {
            await using (var ctx = backend.NewContext())
            {
                await ctx.Database.EnsureCreatedAsync();
                ctx.Widgets.AddRange(
                    new Widget { Name = "high", Rule = new AlphaRule(0.9, 1) },
                    new Widget { Name = "low", Rule = new AlphaRule(0.1, 1) },
                    new Widget { Name = "other", Rule = new BetaRule { Ratio = 5, Label = "b" } });
                await ctx.SaveChangesAsync();
            }

            await using (var read = backend.NewContext())
            {
                // Translated to SQL against the real column; the BetaRule row has a NULL Threshold.
                var names = await read.Widgets
                    .Where(w => EF.Property<double?>(w, "Threshold") >= 0.5)
                    .Select(w => w.Name)
                    .ToListAsync();

                names.ShouldBe(new[] { "high" }, ignoreOrder: false);
            }
        }
    }

    private static async Task RoundTripSharedValue(Backend<GadgetContext> backend)
    {
        await using (var ctx = backend.NewContext())
        {
            await ctx.Database.EnsureCreatedAsync();
            ctx.Gadgets.AddRange(
                new Gadget { Tag = new ColorTag { Value = "col", Priority = 2 } },
                new Gadget { Tag = new SizeTag { Value = "siz", Scale = 3.0 } });
            await ctx.SaveChangesAsync();
        }

        await using (var read = backend.NewContext())
        {
            var tags = await read.Gadgets.OrderBy(g => g.Id).Select(g => g.Tag).ToListAsync();
            tags[0].ShouldBeOfType<ColorTag>().Value.ShouldBe("col", backend.Name);
            tags[1].ShouldBeOfType<SizeTag>().Value.ShouldBe("siz", backend.Name);
        }
    }

    private static async Task RoundTripSharedValueCollapsed(Backend<GadgetCollapsedContext> backend)
    {
        await using (var ctx = backend.NewContext())
        {
            await ctx.Database.EnsureCreatedAsync();
            ctx.Gadgets.AddRange(
                new Gadget { Tag = new ColorTag { Value = "col", Priority = 2 } },
                new Gadget { Tag = new SizeTag { Value = "siz", Scale = 3.0 } });
            await ctx.SaveChangesAsync();
        }

        await using (var read = backend.NewContext())
        {
            var tags = await read.Gadgets.OrderBy(g => g.Id).Select(g => g.Tag).ToListAsync();
            tags[0].ShouldBeOfType<ColorTag>().Value.ShouldBe("col", backend.Name);
            tags[1].ShouldBeOfType<SizeTag>().Value.ShouldBe("siz", backend.Name);
        }
    }

    private IReadOnlyList<string> TableColumns<TContext>()
        where TContext : DbContext
    {
        using var context = (TContext)Activator.CreateInstance(typeof(TContext), BuildSqlite<TContext>())!;

        var differ = context.GetService<IMigrationsModelDiffer>();
        var designModel = context.GetService<IDesignTimeModel>().Model;

        var createTable = differ
            .GetDifferences(source: null, target: designModel.GetRelationalModel())
            .OfType<CreateTableOperation>()
            .Single();

        return createTable.Columns.Select(c => c.Name).ToList();
    }
}
