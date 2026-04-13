using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StepTrail.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTimeoutAndLockExpiry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lock_expires_at",
                table: "workflow_step_executions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "timeout_seconds",
                table: "workflow_definition_steps",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_step_executions_status_lock_expires_at",
                table: "workflow_step_executions",
                columns: new[] { "status", "lock_expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_workflow_step_executions_status_lock_expires_at",
                table: "workflow_step_executions");

            migrationBuilder.DropColumn(
                name: "lock_expires_at",
                table: "workflow_step_executions");

            migrationBuilder.DropColumn(
                name: "timeout_seconds",
                table: "workflow_definition_steps");
        }
    }
}
