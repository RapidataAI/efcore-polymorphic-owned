using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using PolymorphicOwned.EntityFrameworkCore.Tests.Infrastructure;
using PolymorphicOwned.EntityFrameworkCore.Tests.Model;
using Shouldly;
using Xunit;

namespace PolymorphicOwned.EntityFrameworkCore.Tests;

/// <summary>
/// Proves the shadow columns land in a scaffolded migration with no hand-editing, by asking EF's own
/// model differ for the operations it would generate from an empty database.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class MigrationTests(PostgresFixture postgres) : DatabaseTestBase(postgres)
{
    [Fact]
    public void Snake_case_emits_the_motivating_example_columns()
    {
        var columns = AudienceTableColumns(snakeCase: true);

        columns.Keys.ShouldBe(
            new[]
            {
                "id", "name",
                "graduation_rule_type",
                "graduation_score", "demotion_score", "min_responses_to_graduate",
                "target_accuracy", "min_tasks", "max_tasks",
            },
            ignoreOrder: true);

        // Discriminator is non-null (required owner); every subtype-specific column is nullable.
        columns["graduation_rule_type"].IsNullable.ShouldBeFalse();
        columns["graduation_score"].IsNullable.ShouldBeTrue();
        columns["demotion_score"].IsNullable.ShouldBeTrue();
        columns["min_responses_to_graduate"].IsNullable.ShouldBeTrue();
        columns["target_accuracy"].IsNullable.ShouldBeTrue();
        columns["min_tasks"].IsNullable.ShouldBeTrue();
        columns["max_tasks"].IsNullable.ShouldBeTrue();

        UnderlyingType(columns["graduation_score"]).ShouldBe(typeof(double));
        UnderlyingType(columns["min_responses_to_graduate"]).ShouldBe(typeof(int));
    }

    [Fact]
    public void Default_naming_uses_member_names_and_explicit_discriminator()
    {
        var columns = AudienceTableColumns(snakeCase: false);

        columns.Keys.ShouldContain("graduation_rule_type"); // explicit HasDiscriminatorColumn wins
        columns.Keys.ShouldContain("GraduationScore");
        columns.Keys.ShouldContain("MaxTasks");
    }

    [Fact]
    public void Mapping_is_recorded_on_the_model()
    {
        using var context = new AudienceContext(BuildSqlite<AudienceContext>());

        context.Model.FindAnnotation("PolymorphicOwned:Mappings")!.Value
            .ShouldBeOfType<string>()
            .ShouldContain("graduation_rule_type");
    }

    private static Type UnderlyingType(AddColumnOperation column) =>
        Nullable.GetUnderlyingType(column.ClrType) ?? column.ClrType;

    private Dictionary<string, AddColumnOperation> AudienceTableColumns(bool snakeCase)
    {
        using var context = new AudienceContext(BuildSqlite<AudienceContext>(snakeCase));

        var differ = context.GetService<IMigrationsModelDiffer>();
        var designModel = context.GetService<IDesignTimeModel>().Model;
        var operations = differ.GetDifferences(source: null, target: designModel.GetRelationalModel());
        var createTable = operations.OfType<CreateTableOperation>().Single();

        return createTable.Columns.ToDictionary(c => c.Name);
    }
}
