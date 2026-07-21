using Microsoft.EntityFrameworkCore;
using PolymorphicOwned.EntityFrameworkCore;

namespace PolymorphicOwned.EntityFrameworkCore.Tests.Infrastructure;

/// <summary>
/// Base for provider round-trip tests. <see cref="Backends{TContext}"/> yields a context factory per
/// backend (SQLite + Postgres); each factory returns fresh <see cref="DbContext"/> instances bound to
/// the same underlying database, so a write context and a later read context see the same data.
/// </summary>
public abstract class DatabaseTestBase(PostgresFixture postgres)
{
    protected readonly record struct Backend<TContext>(string Name, Func<TContext> NewContext)
        where TContext : DbContext;

    protected IEnumerable<Backend<TContext>> Backends<TContext>(bool snakeCase = false)
        where TContext : DbContext
    {
        yield return new Backend<TContext>("sqlite", Factory<TContext>(BuildSqlite<TContext>(snakeCase)));
        yield return new Backend<TContext>("postgres", Factory<TContext>(BuildPostgres<TContext>(snakeCase)));
    }

    protected DbContextOptions<TContext> BuildSqlite<TContext>(bool snakeCase = false)
        where TContext : DbContext
    {
        var path = Path.Combine(Path.GetTempPath(), $"poly_{Guid.NewGuid():N}.db");
        var builder = new DbContextOptionsBuilder<TContext>().UseSqlite($"Data Source={path}");
        return Finish(builder, snakeCase);
    }

    protected DbContextOptions<TContext> BuildPostgres<TContext>(bool snakeCase = false)
        where TContext : DbContext
    {
        var database = $"poly_{Guid.NewGuid():N}";
        var builder = new DbContextOptionsBuilder<TContext>().UseNpgsql(postgres.GetConnectionString(database));
        return Finish(builder, snakeCase);
    }

    private static DbContextOptions<TContext> Finish<TContext>(
        DbContextOptionsBuilder<TContext> builder,
        bool snakeCase)
        where TContext : DbContext
    {
        if (snakeCase)
        {
            builder.UseSnakeCaseNamingConvention();
        }

        builder.UsePolymorphicOwned();
        return builder.Options;
    }

    private static Func<TContext> Factory<TContext>(DbContextOptions<TContext> options)
        where TContext : DbContext =>
        () => (TContext)Activator.CreateInstance(typeof(TContext), options)!;
}
