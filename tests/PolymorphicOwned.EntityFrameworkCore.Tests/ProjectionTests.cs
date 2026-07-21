using Microsoft.EntityFrameworkCore;
using PolymorphicOwned.EntityFrameworkCore.Tests.Infrastructure;
using PolymorphicOwned.EntityFrameworkCore.Tests.Model;
using Shouldly;
using Xunit;

namespace PolymorphicOwned.EntityFrameworkCore.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ProjectionTests(PostgresFixture postgres) : DatabaseTestBase(postgres)
{
    private sealed class WidgetRuleView
    {
        public int Id { get; set; }

        public IWidgetRule Rule { get; set; } = default!;
    }

    [Fact]
    public async Task Projects_reconstructed_value_into_a_dto()
    {
        foreach (var backend in Backends<WidgetContext>())
        {
            await using (var ctx = backend.NewContext())
            {
                await ctx.Database.EnsureCreatedAsync();
                ctx.Widgets.AddRange(
                    new Widget { Name = "a", Rule = new AlphaRule(0.9, 5) },
                    new Widget { Name = "b", Rule = new BetaRule { Ratio = 1.5, Label = "hi" } },
                    new Widget { Name = "g", Rule = new GammaRule() });
                await ctx.SaveChangesAsync();
            }

            await using (var read = backend.NewContext())
            {
                var views = await read.Widgets
                    .OrderBy(w => w.Id)
                    .Select(w => new WidgetRuleView { Id = w.Id, Rule = w.Rule })
                    .AsNoTracking()
                    .ToListAsync();

                var alpha = views[0].Rule.ShouldBeOfType<AlphaRule>();
                alpha.Threshold.ShouldBe(0.9, backend.Name);
                alpha.Count.ShouldBe(5, backend.Name);

                var beta = views[1].Rule.ShouldBeOfType<BetaRule>();
                beta.Ratio.ShouldBe(1.5, backend.Name);
                beta.Label.ShouldBe("hi", backend.Name);

                views[2].Rule.ShouldBeOfType<GammaRule>();
            }
        }
    }

    [Fact]
    public async Task Projects_the_value_directly_and_alongside_scalars()
    {
        foreach (var backend in Backends<WidgetContext>())
        {
            await using (var ctx = backend.NewContext())
            {
                await ctx.Database.EnsureCreatedAsync();
                ctx.Widgets.Add(new Widget { Name = "solo", Rule = new AlphaRule(0.25, 2) });
                await ctx.SaveChangesAsync();
            }

            await using (var read = backend.NewContext())
            {
                // Value projected directly.
                var rule = await read.Widgets.Select(w => w.Rule).SingleAsync();
                rule.ShouldBeOfType<AlphaRule>().Threshold.ShouldBe(0.25, backend.Name);

                // Alongside a filter + scalar projection on flattened columns.
                var row = await read.Widgets
                    .Where(w => EF.Property<double?>(w, "Threshold") >= 0.2)
                    .Select(w => new { w.Name, w.Rule })
                    .SingleAsync();
                row.Name.ShouldBe("solo", backend.Name);
                row.Rule.ShouldBeOfType<AlphaRule>().Count.ShouldBe(2, backend.Name);
            }
        }
    }

    [Fact]
    public async Task Filters_and_sorts_on_subtype_members_and_is_checks()
    {
        foreach (var backend in Backends<WidgetContext>())
        {
            await using (var ctx = backend.NewContext())
            {
                await ctx.Database.EnsureCreatedAsync();
                ctx.Widgets.AddRange(
                    new Widget { Name = "high", Rule = new AlphaRule(0.9, 1) },
                    new Widget { Name = "low", Rule = new AlphaRule(0.1, 1) },
                    new Widget { Name = "beta", Rule = new BetaRule { Ratio = 5, Label = "b" } });
                await ctx.SaveChangesAsync();
            }

            await using (var read = backend.NewContext())
            {
                // Strongly-typed member access through a subtype cast, translated to the column.
                var strict = await read.Widgets
                    .Where(w => ((AlphaRule)w.Rule).Threshold >= 0.5)
                    .Select(w => w.Name)
                    .ToListAsync();
                strict.ShouldBe(new[] { "high" }, ignoreOrder: false);

                // `is` check, translated to the discriminator column.
                var betas = await read.Widgets
                    .Where(w => w.Rule is BetaRule)
                    .Select(w => w.Name)
                    .ToListAsync();
                betas.ShouldBe(new[] { "beta" }, ignoreOrder: false);

                // OrderBy on a subtype member column.
                var ordered = await read.Widgets
                    .Where(w => w.Rule is AlphaRule)
                    .OrderByDescending(w => ((AlphaRule)w.Rule).Threshold)
                    .Select(w => w.Name)
                    .ToListAsync();
                ordered.ShouldBe(new[] { "high", "low" }, ignoreOrder: false);
            }
        }
    }

    [Fact]
    public async Task Projects_null_for_an_optional_owner_without_a_value()
    {
        foreach (var backend in Backends<GadgetContext>())
        {
            await using (var ctx = backend.NewContext())
            {
                await ctx.Database.EnsureCreatedAsync();
                ctx.Gadgets.AddRange(
                    new Gadget { Tag = null },
                    new Gadget { Tag = new ColorTag { Value = "red", Priority = 1 } });
                await ctx.SaveChangesAsync();
            }

            await using (var read = backend.NewContext())
            {
                var tags = await read.Gadgets
                    .OrderBy(g => g.Id)
                    .Select(g => g.Tag)
                    .ToListAsync();

                tags[0].ShouldBeNull(backend.Name);
                tags[1].ShouldBeOfType<ColorTag>().Value.ShouldBe("red", backend.Name);
            }
        }
    }
}
