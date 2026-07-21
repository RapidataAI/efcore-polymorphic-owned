using Microsoft.EntityFrameworkCore;
using PolymorphicOwned.Sample;

// Round-trips the motivating example against Postgres:
//   Audience { GraduationRule: ScoreThresholdRule | TaskAccuracyRule }
// Set POSTGRES_CONNECTION to point at your database (defaults to localhost:5432).

await using var db = new AudienceDbContext(SampleOptions.Build().Options);

await db.Database.MigrateAsync();
await db.Audiences.ExecuteDeleteAsync();

db.Audiences.AddRange(
    new Audience
    {
        Name = "Reliable reviewers",
        GraduationRule = new ScoreThresholdRule
        {
            GraduationScore = 0.85,
            DemotionScore = 0.4,
            MinResponsesToGraduate = 50,
        },
    },
    new Audience
    {
        Name = "Accurate labelers",
        GraduationRule = new TaskAccuracyRule
        {
            TargetAccuracy = 0.95,
            MinTasks = 20,
            MaxTasks = 200,
        },
    });

await db.SaveChangesAsync();

// Fresh context so nothing is served from the identity map — this proves materialization.
await using var readContext = new AudienceDbContext(SampleOptions.Build().Options);
var audiences = await readContext.Audiences.OrderBy(a => a.Id).ToListAsync();

foreach (var audience in audiences)
{
    var description = audience.GraduationRule switch
    {
        ScoreThresholdRule score =>
            $"ScoreThresholdRule(graduate>={score.GraduationScore}, demote<{score.DemotionScore}, minResponses={score.MinResponsesToGraduate})",
        TaskAccuracyRule accuracy =>
            $"TaskAccuracyRule(target={accuracy.TargetAccuracy}, tasks {accuracy.MinTasks}..{accuracy.MaxTasks})",
        _ => audience.GraduationRule.GetType().Name,
    };

    Console.WriteLine($"#{audience.Id} {audience.Name,-20} -> {description}");
}

// Server-side filter on a flattened column (see the query-limitation caveat in the README).
var strictScoreGates = await readContext.Audiences
    .Where(a => EF.Property<double?>(a, "GraduationScore") >= 0.8)
    .Select(a => a.Name)
    .ToListAsync();

Console.WriteLine($"Audiences with a graduation score >= 0.8: {string.Join(", ", strictScoreGates)}");
