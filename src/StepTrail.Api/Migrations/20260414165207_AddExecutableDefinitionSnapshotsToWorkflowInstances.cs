using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StepTrail.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutableDefinitionSnapshotsToWorkflowInstances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_workflow_instances_workflow_definitions_workflow_definition~",
                table: "workflow_instances");

            migrationBuilder.DropForeignKey(
                name: "FK_workflow_step_executions_workflow_definition_steps_workflow~",
                table: "workflow_step_executions");

            migrationBuilder.AlterColumn<Guid>(
                name: "workflow_definition_step_id",
                table: "workflow_step_executions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "executable_step_definition_id",
                table: "workflow_step_executions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "retry_policy_override_key",
                table: "workflow_step_executions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "step_configuration",
                table: "workflow_step_executions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "step_order",
                table: "workflow_step_executions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "step_type",
                table: "workflow_step_executions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "workflow_definition_id",
                table: "workflow_instances",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "executable_workflow_definition_id",
                table: "workflow_instances",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "workflow_definition_key",
                table: "workflow_instances",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "workflow_definition_version",
                table: "workflow_instances",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_step_executions_workflow_instance_id_step_order_st~",
                table: "workflow_step_executions",
                columns: new[] { "workflow_instance_id", "step_order", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_instances_workflow_definition_key_workflow_definit~",
                table: "workflow_instances",
                columns: new[] { "workflow_definition_key", "workflow_definition_version" });

            migrationBuilder.AddForeignKey(
                name: "FK_workflow_instances_workflow_definitions_workflow_definition~",
                table: "workflow_instances",
                column: "workflow_definition_id",
                principalTable: "workflow_definitions",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_workflow_step_executions_workflow_definition_steps_workflow~",
                table: "workflow_step_executions",
                column: "workflow_definition_step_id",
                principalTable: "workflow_definition_steps",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_workflow_instances_workflow_definitions_workflow_definition~",
                table: "workflow_instances");

            migrationBuilder.DropForeignKey(
                name: "FK_workflow_step_executions_workflow_definition_steps_workflow~",
                table: "workflow_step_executions");

            migrationBuilder.DropIndex(
                name: "IX_workflow_step_executions_workflow_instance_id_step_order_st~",
                table: "workflow_step_executions");

            migrationBuilder.DropIndex(
                name: "IX_workflow_instances_workflow_definition_key_workflow_definit~",
                table: "workflow_instances");

            migrationBuilder.DropColumn(
                name: "executable_step_definition_id",
                table: "workflow_step_executions");

            migrationBuilder.DropColumn(
                name: "retry_policy_override_key",
                table: "workflow_step_executions");

            migrationBuilder.DropColumn(
                name: "step_configuration",
                table: "workflow_step_executions");

            migrationBuilder.DropColumn(
                name: "step_order",
                table: "workflow_step_executions");

            migrationBuilder.DropColumn(
                name: "step_type",
                table: "workflow_step_executions");

            migrationBuilder.DropColumn(
                name: "executable_workflow_definition_id",
                table: "workflow_instances");

            migrationBuilder.DropColumn(
                name: "workflow_definition_key",
                table: "workflow_instances");

            migrationBuilder.DropColumn(
                name: "workflow_definition_version",
                table: "workflow_instances");

            migrationBuilder.AlterColumn<Guid>(
                name: "workflow_definition_step_id",
                table: "workflow_step_executions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "workflow_definition_id",
                table: "workflow_instances",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_workflow_instances_workflow_definitions_workflow_definition~",
                table: "workflow_instances",
                column: "workflow_definition_id",
                principalTable: "workflow_definitions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_workflow_step_executions_workflow_definition_steps_workflow~",
                table: "workflow_step_executions",
                column: "workflow_definition_step_id",
                principalTable: "workflow_definition_steps",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
