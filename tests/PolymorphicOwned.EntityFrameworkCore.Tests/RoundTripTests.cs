using Microsoft.EntityFrameworkCore;
using PolymorphicOwned.EntityFrameworkCore.Tests.Infrastructure;
using PolymorphicOwned.EntityFrameworkCore.Tests.Model;
using Shouldly;
using Xunit;

namespace PolymorphicOwned.EntityFrameworkCore.Tests;

[Collection(PostgresCollection.Name)]
public sealed class RoundTripTests(PostgresFixture postgres) : DatabaseTestBase(postgres)
{
    [Fact]
    public async Task Reads_back_each_subtype_as_its_concrete_type_and_values()
    {
        foreach (var backend in Backends<WidgetContext>())
        {
            await EnsureCreated(backend);

            int alphaId, betaId, gammaId;
            await using (var ctx = backend.NewContext())
            {
                var alpha = new Widget { Name = "a", Rule = new AlphaRule(0.9, 5) };
                var beta = new Widget { Name = "b", Rule = new BetaRule { Ratio = 1.5, Label = "hi" } };
                var gamma = new Widget { Name = "g", Rule = new GammaRule() };
                ctx.Widgets.AddRange(alpha, beta, gamma);
                await ctx.SaveChangesAsync();
                (alphaId, betaId, gammaId) = (alpha.Id, beta.Id, gamma.Id);
            }

            await using (var read = backend.NewContext())
            {
                var alpha = await read.Widgets.SingleAsync(w => w.Id == alphaId);
                var alphaRule = alpha.Rule.ShouldBeOfType<AlphaRule>();
                alphaRule.Threshold.ShouldBe(0.9, backend.Name);
                alphaRule.Count.ShouldBe(5, backend.Name);

                var beta = await read.Widgets.SingleAsync(w => w.Id == betaId);
                var betaRule = beta.Rule.ShouldBeOfType<BetaRule>();
                betaRule.Ratio.ShouldBe(1.5, backend.Name);
                betaRule.Label.ShouldBe("hi", backend.Name);

                // Member-less subtype is identified purely by the discriminator.
                var gamma = await read.Widgets.SingleAsync(w => w.Id == gammaId);
                gamma.Rule.ShouldBeOfType<GammaRule>();
            }
        }
    }

    [Fact]
    public async Task Updates_a_member_in_place()
    {
        foreach (var backend in Backends<WidgetContext>())
        {
            await EnsureCreated(backend);

            var id = await Insert(backend, new Widget { Name = "w", Rule = new BetaRule { Ratio = 1.0, Label = "x" } });

            await using (var ctx = backend.NewContext())
            {
                var widget = await ctx.Widgets.SingleAsync(w => w.Id == id);
                ((BetaRule)widget.Rule).Ratio = 42.0;
                await ctx.SaveChangesAsync();
            }

            await using (var read = backend.NewContext())
            {
                var widget = await read.Widgets.SingleAsync(w => w.Id == id);
                ((BetaRule)widget.Rule).Ratio.ShouldBe(42.0, backend.Name);
            }
        }
    }

    [Fact]
    public async Task Swaps_the_subtype()
    {
        foreach (var backend in Backends<WidgetContext>())
        {
            await EnsureCreated(backend);

            var id = await Insert(backend, new Widget { Name = "w", Rule = new AlphaRule(0.1, 1) });

            await using (var ctx = backend.NewContext())
            {
                var widget = await ctx.Widgets.SingleAsync(w => w.Id == id);
                widget.Rule = new BetaRule { Ratio = 9.0, Label = "swapped" };
                await ctx.SaveChangesAsync();
            }

            await using (var read = backend.NewContext())
            {
                var widget = await read.Widgets.SingleAsync(w => w.Id == id);
                var betaRule = widget.Rule.ShouldBeOfType<BetaRule>();
                betaRule.Label.ShouldBe("swapped", backend.Name);

                // The previous subtype's column must be nulled out after the swap.
                var oldColumn = await read.Widgets
                    .Where(w => w.Id == id)
                    .Select(w => EF.Property<double?>(w, "Threshold"))
                    .SingleAsync();
                oldColumn.ShouldBeNull(backend.Name);
            }
        }
    }

    [Fact]
    public async Task Sets_and_clears_an_optional_owner()
    {
        foreach (var backend in Backends<GadgetContext>())
        {
            await EnsureCreated(backend);

            int id;
            await using (var ctx = backend.NewContext())
            {
                var gadget = new Gadget { Tag = null };
                ctx.Gadgets.Add(gadget);
                await ctx.SaveChangesAsync();
                id = gadget.Id;
            }

            await using (var read = backend.NewContext())
            {
                (await read.Gadgets.SingleAsync(g => g.Id == id)).Tag.ShouldBeNull(backend.Name);
            }

            await using (var ctx = backend.NewContext())
            {
                var gadget = await ctx.Gadgets.SingleAsync(g => g.Id == id);
                gadget.Tag = new ColorTag { Value = "red", Priority = 3 };
                await ctx.SaveChangesAsync();
            }

            await using (var read = backend.NewContext())
            {
                var tag = (await read.Gadgets.SingleAsync(g => g.Id == id)).Tag.ShouldBeOfType<ColorTag>();
                tag.Value.ShouldBe("red", backend.Name);
            }

            await using (var ctx = backend.NewContext())
            {
                var gadget = await ctx.Gadgets.SingleAsync(g => g.Id == id);
                gadget.Tag = null;
                await ctx.SaveChangesAsync();
            }

            await using (var read = backend.NewContext())
            {
                (await read.Gadgets.SingleAsync(g => g.Id == id)).Tag.ShouldBeNull(backend.Name);
            }
        }
    }

    private static async Task EnsureCreated<TContext>(Backend<TContext> backend)
        where TContext : DbContext
    {
        await using var ctx = backend.NewContext();
        await ctx.Database.EnsureCreatedAsync();
    }

    private static async Task<int> Insert(Backend<WidgetContext> backend, Widget widget)
    {
        await using var ctx = backend.NewContext();
        ctx.Widgets.Add(widget);
        await ctx.SaveChangesAsync();
        return widget.Id;
    }
}
