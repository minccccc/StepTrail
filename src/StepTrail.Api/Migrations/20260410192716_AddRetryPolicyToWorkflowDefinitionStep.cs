using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StepTrail.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRetryPolicyToWorkflowDefinitionStep : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "max_attempts",
                table: "workflow_definition_steps",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<int>(
                name: "retry_delay_seconds",
                table: "workflow_definition_steps",
                type: "integer",
                nullable: false,
                defaultValue: 30);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "max_attempts",
                table: "workflow_definition_steps");

            migrationBuilder.DropColumn(
                name: "retry_delay_seconds",
                table: "workflow_definition_steps");
        }
    }
}
