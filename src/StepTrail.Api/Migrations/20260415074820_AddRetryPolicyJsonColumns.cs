using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StepTrail.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRetryPolicyJsonColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "retry_policy_json",
                table: "workflow_step_executions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "retry_policy_json",
                table: "executable_step_definitions",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "retry_policy_json",
                table: "workflow_step_executions");

            migrationBuilder.DropColumn(
                name: "retry_policy_json",
                table: "executable_step_definitions");
        }
    }
}
