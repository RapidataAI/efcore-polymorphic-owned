using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PolymorphicOwned.EntityFrameworkCore;

namespace PolymorphicOwned.Sample;

public sealed class AudienceDbContext(DbContextOptions<AudienceDbContext> options) : DbContext(options)
{
    public DbSet<Audience> Audiences => Set<Audience>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Audience>(audience =>
        {
            audience.HasKey(a => a.Id);
            audience.Property(a => a.Name).HasMaxLength(200);

            audience.OwnsPolymorphic(a => a.GraduationRule, poly =>
            {
                poly.HasDiscriminatorColumn("graduation_rule_type");
                poly.HasDerivedType<ScoreThresholdRule>("score_threshold");
                poly.HasDerivedType<TaskAccuracyRule>("task_accuracy");
            });
        });
    }
}

/// <summary>
/// Lets <c>dotnet ef</c> build the context at design time (for <c>migrations add</c>) without a
/// running database. The connection string is only used when the sample actually talks to Postgres.
/// </summary>
public sealed class AudienceDbContextFactory : IDesignTimeDbContextFactory<AudienceDbContext>
{
    public AudienceDbContext CreateDbContext(string[] args) =>
        new(SampleOptions.Build().Options);
}

internal static class SampleOptions
{
    public const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=polymorphic_owned_sample;Username=postgres;Password=postgres";

    public static DbContextOptionsBuilder<AudienceDbContext> Build(string? connectionString = null)
    {
        var connection = connectionString
            ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION")
            ?? DefaultConnectionString;

        return new DbContextOptionsBuilder<AudienceDbContext>()
            .UseNpgsql(connection)
            .UseSnakeCaseNamingConvention()
            .UsePolymorphicOwned();
    }
}
