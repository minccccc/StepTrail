using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StepTrail.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkflowSecrets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "workflow_secrets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_secrets", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_secrets_name",
                table: "workflow_secrets",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "workflow_secrets");
        }
    }
}
