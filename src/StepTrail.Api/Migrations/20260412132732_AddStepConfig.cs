using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StepTrail.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStepConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "config",
                table: "workflow_definition_steps",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "config",
                table: "workflow_definition_steps");
        }
    }
}
