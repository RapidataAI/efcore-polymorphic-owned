using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PolymorphicOwned.EntityFrameworkCore.Configuration;
using PolymorphicOwned.EntityFrameworkCore.Interceptors;
using PolymorphicOwned.EntityFrameworkCore.Query;

namespace PolymorphicOwned.EntityFrameworkCore;

/// <summary>
/// Entry points for mapping a polymorphic owned value object to inline columns on the owner's table.
/// </summary>
public static class PolymorphicOwnedExtensions
{
    // Stateless interceptors, shared across every options instance so EF can reuse its internal
    // service-provider cache (distinct instances would defeat it and trip ManyServiceProvidersCreated).
    private static readonly PolymorphicMaterializationInterceptor MaterializationInterceptor = new();
    private static readonly PolymorphicSaveChangesInterceptor SaveChangesInterceptor = new();
    private static readonly PolymorphicProjectionInterceptor ProjectionInterceptor = new();

    /// <summary>
    /// Maps a polymorphic owned value object (an interface or abstract base with concrete subtypes)
    /// to a discriminator column plus the union of the subtypes' scalar members as nullable columns
    /// on the owner's table.
    /// </summary>
    public static EntityTypeBuilder<TEntity> OwnsPolymorphic<TEntity, TValue>(
        this EntityTypeBuilder<TEntity> builder,
        Expression<Func<TEntity, TValue?>> navigation,
        Action<PolymorphicOwnedBuilder<TValue>> configure)
        where TEntity : class
        where TValue : class
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(configure);

        var navigationProperty = ResolveNavigation(navigation);
        var polyBuilder = new PolymorphicOwnedBuilder<TValue>(navigationProperty);
        configure(polyBuilder);
        polyBuilder.Apply(builder);
        return builder;
    }

    /// <summary>
    /// Registers the interceptors that read, write, and project the flattened polymorphic columns:
    /// materialization (read), save-changes (flatten on write), and query (rewrites a projection of
    /// the polymorphic property, e.g. <c>o.Discount</c> in a <c>Select</c>, into a column-only read +
    /// reconstruction). Call this from <c>OnConfiguring</c> or <c>AddDbContext</c>.
    /// </summary>
    public static DbContextOptionsBuilder UsePolymorphicOwned(this DbContextOptionsBuilder options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.AddInterceptors(MaterializationInterceptor, SaveChangesInterceptor, ProjectionInterceptor);
    }

    /// <summary>
    /// Generic overload that preserves the strongly-typed options builder so it composes with a
    /// <c>DbContextOptionsBuilder&lt;TContext&gt;</c> chain.
    /// </summary>
    public static DbContextOptionsBuilder<TContext> UsePolymorphicOwned<TContext>(
        this DbContextOptionsBuilder<TContext> options)
        where TContext : DbContext
    {
        UsePolymorphicOwned((DbContextOptionsBuilder)options);
        return options;
    }

    private static PropertyInfo ResolveNavigation<TEntity, TValue>(
        Expression<Func<TEntity, TValue?>> navigation)
    {
        var body = navigation.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } unary)
        {
            body = unary.Operand;
        }

        if (body is MemberExpression { Member: PropertyInfo property })
        {
            return property;
        }

        throw new ArgumentException(
            "The navigation must be a simple property access, e.g. o => o.Discount.",
            nameof(navigation));
    }
}
