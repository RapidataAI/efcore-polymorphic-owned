using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PolymorphicOwned.Sample.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audiences",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    demotion_score = table.Column<double>(type: "double precision", nullable: true),
                    graduation_score = table.Column<double>(type: "double precision", nullable: true),
                    max_tasks = table.Column<int>(type: "integer", nullable: true),
                    min_responses_to_graduate = table.Column<int>(type: "integer", nullable: true),
                    min_tasks = table.Column<int>(type: "integer", nullable: true),
                    target_accuracy = table.Column<double>(type: "double precision", nullable: true),
                    graduation_rule_type = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audiences", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audiences");
        }
    }
}
