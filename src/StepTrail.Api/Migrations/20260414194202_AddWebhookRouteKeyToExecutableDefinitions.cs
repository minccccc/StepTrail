using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StepTrail.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookRouteKeyToExecutableDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "webhook_route_key",
                table: "executable_workflow_definitions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE executable_workflow_definitions AS definitions
                SET webhook_route_key = triggers.configuration ->> 'routeKey'
                FROM executable_trigger_definitions AS triggers
                WHERE triggers.workflow_definition_id = definitions.id
                  AND triggers.type = 'Webhook';
                """);

            migrationBuilder.CreateIndex(
                name: "ux_executable_workflow_definitions_active_webhook_route_key",
                table: "executable_workflow_definitions",
                column: "webhook_route_key",
                unique: true,
                filter: "\"status\" = 'Active' AND \"webhook_route_key\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_executable_workflow_definitions_active_webhook_route_key",
                table: "executable_workflow_definitions");

            migrationBuilder.DropColumn(
                name: "webhook_route_key",
                table: "executable_workflow_definitions");
        }
    }
}
