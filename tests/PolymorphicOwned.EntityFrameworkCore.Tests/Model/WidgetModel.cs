using Microsoft.EntityFrameworkCore;
using PolymorphicOwned.EntityFrameworkCore;

namespace PolymorphicOwned.EntityFrameworkCore.Tests.Model;

public interface IWidgetRule;

/// <summary>Immutable/record subtype — exercises constructor-based activation.</summary>
public sealed record AlphaRule(double Threshold, int Count) : IWidgetRule;

/// <summary>Mutable subtype — exercises setter-based activation.</summary>
public sealed class BetaRule : IWidgetRule
{
    public double Ratio { get; set; }

    public string Label { get; set; } = string.Empty;
}

/// <summary>Subtype with no extra scalar members — only the discriminator distinguishes it.</summary>
public sealed class GammaRule : IWidgetRule;

public sealed class Widget
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public IWidgetRule Rule { get; set; } = default!;
}

/// <summary>Three subtypes (one immutable, one member-less), required owner, no naming convention.</summary>
public sealed class WidgetContext(DbContextOptions<WidgetContext> options) : DbContext(options)
{
    public DbSet<Widget> Widgets => Set<Widget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Widget>(widget =>
        {
            widget.HasKey(w => w.Id);
            widget.OwnsPolymorphic(w => w.Rule, poly =>
            {
                poly.HasDerivedType<AlphaRule>("alpha");
                poly.HasDerivedType<BetaRule>("beta");
                poly.HasDerivedType<GammaRule>("gamma");
            });
        });
    }
}
