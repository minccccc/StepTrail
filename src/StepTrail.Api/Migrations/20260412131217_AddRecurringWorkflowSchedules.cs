using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StepTrail.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringWorkflowSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recurring_workflow_schedules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    interval_seconds = table.Column<int>(type: "integer", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    input = table.Column<string>(type: "jsonb", nullable: true),
                    last_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    next_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recurring_workflow_schedules", x => x.id);
                    table.ForeignKey(
                        name: "FK_recurring_workflow_schedules_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_recurring_workflow_schedules_workflow_definitions_workflow_~",
                        column: x => x.workflow_definition_id,
                        principalTable: "workflow_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_recurring_workflow_schedules_is_enabled_next_run_at",
                table: "recurring_workflow_schedules",
                columns: new[] { "is_enabled", "next_run_at" });

            migrationBuilder.CreateIndex(
                name: "IX_recurring_workflow_schedules_tenant_id",
                table: "recurring_workflow_schedules",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_recurring_workflow_schedules_workflow_definition_id",
                table: "recurring_workflow_schedules",
                column: "workflow_definition_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "recurring_workflow_schedules");
        }
    }
}
