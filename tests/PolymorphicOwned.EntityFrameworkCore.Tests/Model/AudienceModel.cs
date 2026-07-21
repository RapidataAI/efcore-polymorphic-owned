using Microsoft.EntityFrameworkCore;
using PolymorphicOwned.EntityFrameworkCore;

namespace PolymorphicOwned.EntityFrameworkCore.Tests.Model;

/// <summary>Abstract-class base (not an interface) — proves that path is supported end to end.</summary>
public abstract class GraduationRule;

/// <summary>
/// Immutable subtype with get-only properties and a matching constructor — exercises
/// constructor-based activation over an abstract base.
/// </summary>
public sealed class ScoreThresholdRule(double graduationScore, double demotionScore, int minResponsesToGraduate)
    : GraduationRule
{
    public double GraduationScore { get; } = graduationScore;

    public double DemotionScore { get; } = demotionScore;

    public int MinResponsesToGraduate { get; } = minResponsesToGraduate;
}

/// <summary>Mutable subtype — exercises setter-based activation over an abstract base.</summary>
public sealed class TaskAccuracyRule : GraduationRule
{
    public double TargetAccuracy { get; set; }

    public int MinTasks { get; set; }

    public int MaxTasks { get; set; }
}

public sealed class Audience
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public GraduationRule GraduationRule { get; set; } = default!;
}

/// <summary>
/// The motivating example, with the snake_case naming convention enabled — used to assert the exact
/// migration columns, that an abstract base round-trips, and that naming-convention plugins rewrite
/// the shadow columns.
/// </summary>
public sealed class AudienceContext(DbContextOptions<AudienceContext> options) : DbContext(options)
{
    public DbSet<Audience> Audiences => Set<Audience>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Audience>(audience =>
        {
            audience.HasKey(a => a.Id);
            audience.OwnsPolymorphic(a => a.GraduationRule, poly =>
            {
                poly.HasDiscriminatorColumn("graduation_rule_type");
                poly.HasDerivedType<ScoreThresholdRule>("score_threshold");
                poly.HasDerivedType<TaskAccuracyRule>("task_accuracy");
            });
        });
    }
}
