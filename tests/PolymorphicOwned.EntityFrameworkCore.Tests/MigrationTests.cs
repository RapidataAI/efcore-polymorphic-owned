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
        var columns = OrderTableColumns(snakeCase: true);

        columns.Keys.ShouldBe(
            new[]
            {
                "id", "reference",
                "discount_type",
                "percentage", "max_amount", "min_items",
                "amount", "min_order_total", "max_redemptions",
            },
            ignoreOrder: true);

        // Discriminator is non-null (required owner); every subtype-specific column is nullable.
        columns["discount_type"].IsNullable.ShouldBeFalse();
        columns["percentage"].IsNullable.ShouldBeTrue();
        columns["max_amount"].IsNullable.ShouldBeTrue();
        columns["min_items"].IsNullable.ShouldBeTrue();
        columns["amount"].IsNullable.ShouldBeTrue();
        columns["min_order_total"].IsNullable.ShouldBeTrue();
        columns["max_redemptions"].IsNullable.ShouldBeTrue();

        UnderlyingType(columns["percentage"]).ShouldBe(typeof(double));
        UnderlyingType(columns["min_items"]).ShouldBe(typeof(int));
    }

    [Fact]
    public void Default_naming_uses_member_names_and_explicit_discriminator()
    {
        var columns = OrderTableColumns(snakeCase: false);

        columns.Keys.ShouldContain("discount_type"); // explicit HasDiscriminatorColumn wins
        columns.Keys.ShouldContain("Percentage");
        columns.Keys.ShouldContain("MaxRedemptions");
    }

    [Fact]
    public void Mapping_is_recorded_on_the_model()
    {
        using var context = new OrderContext(BuildSqlite<OrderContext>());

        context.Model.FindAnnotation("PolymorphicOwned:Mappings")!.Value
            .ShouldBeOfType<string>()
            .ShouldContain("discount_type");
    }

    private static Type UnderlyingType(AddColumnOperation column) =>
        Nullable.GetUnderlyingType(column.ClrType) ?? column.ClrType;

    private Dictionary<string, AddColumnOperation> OrderTableColumns(bool snakeCase)
    {
        using var context = new OrderContext(BuildSqlite<OrderContext>(snakeCase));

        var differ = context.GetService<IMigrationsModelDiffer>();
        var designModel = context.GetService<IDesignTimeModel>().Model;
        var operations = differ.GetDifferences(source: null, target: designModel.GetRelationalModel());
        var createTable = operations.OfType<CreateTableOperation>().Single();

        return createTable.Columns.ToDictionary(c => c.Name);
    }
}
