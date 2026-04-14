using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StepTrail.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutableDefinitionPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "executable_workflow_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_executable_workflow_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "executable_step_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    configuration = table.Column<string>(type: "jsonb", nullable: false),
                    retry_policy_override_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_executable_step_definitions", x => x.id);
                    table.ForeignKey(
                        name: "FK_executable_step_definitions_executable_workflow_definitions~",
                        column: x => x.workflow_definition_id,
                        principalTable: "executable_workflow_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "executable_trigger_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    configuration = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_executable_trigger_definitions", x => x.id);
                    table.ForeignKey(
                        name: "FK_executable_trigger_definitions_executable_workflow_definiti~",
                        column: x => x.workflow_definition_id,
                        principalTable: "executable_workflow_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_executable_step_definitions_workflow_definition_id_key",
                table: "executable_step_definitions",
                columns: new[] { "workflow_definition_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_executable_step_definitions_workflow_definition_id_order",
                table: "executable_step_definitions",
                columns: new[] { "workflow_definition_id", "order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_executable_trigger_definitions_workflow_definition_id",
                table: "executable_trigger_definitions",
                column: "workflow_definition_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_executable_workflow_definitions_key_status",
                table: "executable_workflow_definitions",
                columns: new[] { "key", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_executable_workflow_definitions_key_version",
                table: "executable_workflow_definitions",
                columns: new[] { "key", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "executable_step_definitions");

            migrationBuilder.DropTable(
                name: "executable_trigger_definitions");

            migrationBuilder.DropTable(
                name: "executable_workflow_definitions");
        }
    }
}
