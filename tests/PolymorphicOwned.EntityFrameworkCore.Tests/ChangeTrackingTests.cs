using Microsoft.EntityFrameworkCore;
using PolymorphicOwned.EntityFrameworkCore.Tests.Infrastructure;
using PolymorphicOwned.EntityFrameworkCore.Tests.Model;
using Shouldly;
using Xunit;

namespace PolymorphicOwned.EntityFrameworkCore.Tests;

[Collection(PostgresCollection.Name)]
public sealed class ChangeTrackingTests(PostgresFixture postgres) : DatabaseTestBase(postgres)
{
    [Fact]
    public async Task No_update_when_value_object_is_unchanged()
    {
        var options = BuildSqlite<WidgetContext>();
        var id = await Seed(options, new BetaRule { Ratio = 1.0, Label = "x" });

        await using var ctx = new WidgetContext(options);
        _ = await ctx.Widgets.SingleAsync(w => w.Id == id);

        (await ctx.SaveChangesAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Updates_when_a_member_changes()
    {
        var options = BuildSqlite<WidgetContext>();
        var id = await Seed(options, new BetaRule { Ratio = 1.0, Label = "x" });

        await using var ctx = new WidgetContext(options);
        var widget = await ctx.Widgets.SingleAsync(w => w.Id == id);
        ((BetaRule)widget.Rule).Ratio = 2.0;

        (await ctx.SaveChangesAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Updates_when_the_subtype_is_swapped()
    {
        var options = BuildSqlite<WidgetContext>();
        var id = await Seed(options, new BetaRule { Ratio = 1.0, Label = "x" });

        await using var ctx = new WidgetContext(options);
        var widget = await ctx.Widgets.SingleAsync(w => w.Id == id);
        widget.Rule = new AlphaRule(0.5, 2);

        (await ctx.SaveChangesAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Required_owner_rejects_null()
    {
        var options = BuildSqlite<WidgetContext>();

        await using var ctx = new WidgetContext(options);
        await ctx.Database.EnsureCreatedAsync();
        ctx.Widgets.Add(new Widget { Name = "bad", Rule = null! });

        await Should.ThrowAsync<InvalidOperationException>(() => ctx.SaveChangesAsync());
    }

    private static async Task<int> Seed(DbContextOptions<WidgetContext> options, IWidgetRule rule)
    {
        await using var ctx = new WidgetContext(options);
        await ctx.Database.EnsureCreatedAsync();
        var widget = new Widget { Name = "w", Rule = rule };
        ctx.Widgets.Add(widget);
        await ctx.SaveChangesAsync();
        return widget.Id;
    }
}
