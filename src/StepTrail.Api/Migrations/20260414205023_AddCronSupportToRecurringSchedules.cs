using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StepTrail.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCronSupportToRecurringSchedules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "interval_seconds",
                table: "recurring_workflow_schedules",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "cron_expression",
                table: "recurring_workflow_schedules",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_recurring_workflow_schedules_schedule_mode",
                table: "recurring_workflow_schedules",
                sql: "(interval_seconds IS NOT NULL AND cron_expression IS NULL) OR (interval_seconds IS NULL AND cron_expression IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_recurring_workflow_schedules_schedule_mode",
                table: "recurring_workflow_schedules");

            migrationBuilder.DropColumn(
                name: "cron_expression",
                table: "recurring_workflow_schedules");

            migrationBuilder.AlterColumn<int>(
                name: "interval_seconds",
                table: "recurring_workflow_schedules",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
