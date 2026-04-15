using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StepTrail.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "alert_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    workflow_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    step_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    cause = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    generated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_records", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "alert_delivery_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    attempted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alert_delivery_records", x => x.id);
                    table.ForeignKey(
                        name: "FK_alert_delivery_records_alert_records_alert_record_id",
                        column: x => x.alert_record_id,
                        principalTable: "alert_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alert_delivery_records_alert_record_id",
                table: "alert_delivery_records",
                column: "alert_record_id");

            migrationBuilder.CreateIndex(
                name: "IX_alert_records_generated_at_utc",
                table: "alert_records",
                column: "generated_at_utc");

            migrationBuilder.CreateIndex(
                name: "IX_alert_records_workflow_instance_id",
                table: "alert_records",
                column: "workflow_instance_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alert_delivery_records");

            migrationBuilder.DropTable(
                name: "alert_records");
        }
    }
}
