using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StepTrail.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceTemplateMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source_template_key",
                table: "executable_workflow_definitions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source_template_version",
                table: "executable_workflow_definitions",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "source_template_key",
                table: "executable_workflow_definitions");

            migrationBuilder.DropColumn(
                name: "source_template_version",
                table: "executable_workflow_definitions");
        }
    }
}
