using Microsoft.EntityFrameworkCore;
using PolymorphicOwned.EntityFrameworkCore;

namespace PolymorphicOwned.EntityFrameworkCore.Tests.Model;

public interface IGadgetTag;

public sealed class ColorTag : IGadgetTag
{
    public string Value { get; set; } = string.Empty;

    public int Priority { get; set; }
}

public sealed class SizeTag : IGadgetTag
{
    public string Value { get; set; } = string.Empty;

    public double Scale { get; set; }
}

public sealed class Gadget
{
    public int Id { get; set; }

    // Optional owner — may be null.
    public IGadgetTag? Tag { get; set; }
}

/// <summary>Optional owner; the shared <c>Value</c> member maps to subtype-qualified columns (default).</summary>
public sealed class GadgetContext(DbContextOptions<GadgetContext> options) : DbContext(options)
{
    public DbSet<Gadget> Gadgets => Set<Gadget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Gadget>(gadget =>
        {
            gadget.HasKey(g => g.Id);
            gadget.OwnsPolymorphic(g => g.Tag, poly =>
            {
                poly.IsOptional();
                poly.HasDerivedType<ColorTag>("color");
                poly.HasDerivedType<SizeTag>("size");
            });
        });
    }
}

/// <summary>Same shape as <see cref="GadgetContext"/> but collapses the shared <c>Value</c> member.</summary>
public sealed class GadgetCollapsedContext(DbContextOptions<GadgetCollapsedContext> options) : DbContext(options)
{
    public DbSet<Gadget> Gadgets => Set<Gadget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Gadget>(gadget =>
        {
            gadget.HasKey(g => g.Id);
            gadget.OwnsPolymorphic(g => g.Tag, poly =>
            {
                poly.IsOptional();
                poly.CollapseSharedMembers();
                poly.HasDerivedType<ColorTag>("color");
                poly.HasDerivedType<SizeTag>("size");
            });
        });
    }
}
