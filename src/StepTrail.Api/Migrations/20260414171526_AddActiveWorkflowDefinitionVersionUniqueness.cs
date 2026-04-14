using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StepTrail.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddActiveWorkflowDefinitionVersionUniqueness : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_executable_workflow_definitions_active_key",
                table: "executable_workflow_definitions",
                column: "key",
                unique: true,
                filter: "\"status\" = 'Active'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_executable_workflow_definitions_active_key",
                table: "executable_workflow_definitions");
        }
    }
}
