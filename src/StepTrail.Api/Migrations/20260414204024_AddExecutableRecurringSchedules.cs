using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StepTrail.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutableRecurringSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_recurring_workflow_schedules_workflow_definitions_workflow_~",
                table: "recurring_workflow_schedules");

            migrationBuilder.AlterColumn<Guid>(
                name: "workflow_definition_id",
                table: "recurring_workflow_schedules",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "executable_workflow_key",
                table: "recurring_workflow_schedules",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_recurring_workflow_schedules_executable_workflow_key",
                table: "recurring_workflow_schedules",
                column: "executable_workflow_key",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_recurring_workflow_schedules_target",
                table: "recurring_workflow_schedules",
                sql: "(workflow_definition_id IS NOT NULL AND executable_workflow_key IS NULL) OR (workflow_definition_id IS NULL AND executable_workflow_key IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_recurring_workflow_schedules_workflow_definitions_workflow_~",
                table: "recurring_workflow_schedules",
                column: "workflow_definition_id",
                principalTable: "workflow_definitions",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_recurring_workflow_schedules_workflow_definitions_workflow_~",
                table: "recurring_workflow_schedules");

            migrationBuilder.DropIndex(
                name: "IX_recurring_workflow_schedules_executable_workflow_key",
                table: "recurring_workflow_schedules");

            migrationBuilder.DropCheckConstraint(
                name: "CK_recurring_workflow_schedules_target",
                table: "recurring_workflow_schedules");

            migrationBuilder.DropColumn(
                name: "executable_workflow_key",
                table: "recurring_workflow_schedules");

            migrationBuilder.AlterColumn<Guid>(
                name: "workflow_definition_id",
                table: "recurring_workflow_schedules",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_recurring_workflow_schedules_workflow_definitions_workflow_~",
                table: "recurring_workflow_schedules",
                column: "workflow_definition_id",
                principalTable: "workflow_definitions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
