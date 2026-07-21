using Microsoft.EntityFrameworkCore;
using PolymorphicOwned.EntityFrameworkCore.Tests.Infrastructure;
using PolymorphicOwned.EntityFrameworkCore.Tests.Model;
using Shouldly;
using Xunit;

namespace PolymorphicOwned.EntityFrameworkCore.Tests;

/// <summary>
/// The motivating example with an <b>abstract-class</b> base (not an interface): a record subtype
/// (constructor activation) and a mutable subtype (setter activation) round-trip on both backends.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AbstractBaseTests(PostgresFixture postgres) : DatabaseTestBase(postgres)
{
    [Fact]
    public async Task Reads_back_each_subtype_of_an_abstract_base_as_its_concrete_type()
    {
        foreach (var backend in Backends<AudienceContext>(snakeCase: true))
        {
            int scoreId, accuracyId;
            await using (var ctx = backend.NewContext())
            {
                await ctx.Database.EnsureCreatedAsync();

                var score = new Audience
                {
                    Name = "reviewers",
                    GraduationRule = new ScoreThresholdRule(0.85, 0.4, 50),
                };
                var accuracy = new Audience
                {
                    Name = "labelers",
                    GraduationRule = new TaskAccuracyRule { TargetAccuracy = 0.95, MinTasks = 20, MaxTasks = 200 },
                };
                ctx.Audiences.AddRange(score, accuracy);
                await ctx.SaveChangesAsync();
                (scoreId, accuracyId) = (score.Id, accuracy.Id);
            }

            await using (var read = backend.NewContext())
            {
                var score = await read.Audiences.SingleAsync(a => a.Id == scoreId);
                var scoreRule = score.GraduationRule.ShouldBeOfType<ScoreThresholdRule>();
                scoreRule.GraduationScore.ShouldBe(0.85, backend.Name);
                scoreRule.DemotionScore.ShouldBe(0.4, backend.Name);
                scoreRule.MinResponsesToGraduate.ShouldBe(50, backend.Name);

                var accuracy = await read.Audiences.SingleAsync(a => a.Id == accuracyId);
                var accuracyRule = accuracy.GraduationRule.ShouldBeOfType<TaskAccuracyRule>();
                accuracyRule.TargetAccuracy.ShouldBe(0.95, backend.Name);
                accuracyRule.MinTasks.ShouldBe(20, backend.Name);
                accuracyRule.MaxTasks.ShouldBe(200, backend.Name);

                // The inactive subtype's columns are NULL — filter server-side on the real column.
                var strict = await read.Audiences
                    .Where(a => EF.Property<double?>(a, "GraduationScore") >= 0.8)
                    .Select(a => a.Name)
                    .ToListAsync();
                strict.ShouldBe(new[] { "reviewers" });
            }
        }
    }
}
