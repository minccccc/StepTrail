using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StepTrail.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPilotTelemetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pilot_telemetry_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    workflow_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    workflow_definition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    workflow_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trigger_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    step_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    actor_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pilot_telemetry_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pilot_telemetry_events_category",
                table: "pilot_telemetry_events",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "IX_pilot_telemetry_events_event_name",
                table: "pilot_telemetry_events",
                column: "event_name");

            migrationBuilder.CreateIndex(
                name: "IX_pilot_telemetry_events_occurred_at_utc",
                table: "pilot_telemetry_events",
                column: "occurred_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pilot_telemetry_events");
        }
    }
}
