using Testcontainers.PostgreSql;
using Xunit;

namespace PolymorphicOwned.EntityFrameworkCore.Tests.Infrastructure;

/// <summary>
/// Starts a throwaway PostgreSQL container once for the whole "postgres" collection. Each test gets
/// its own freshly-created database (unique name) for isolation.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:16-alpine").Build();

    public string GetConnectionString(string database)
    {
        // Swap the default database in the container's connection string for a per-test one.
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = database,
        };
        return builder.ConnectionString;
    }

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
