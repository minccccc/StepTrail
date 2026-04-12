using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StepTrail.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTenantFromWorkflowDefinition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_workflow_definitions_tenants_tenant_id",
                table: "workflow_definitions");

            migrationBuilder.DropIndex(
                name: "IX_workflow_definitions_tenant_id_key_version",
                table: "workflow_definitions");

            migrationBuilder.DropColumn(
                name: "tenant_id",
                table: "workflow_definitions");

            migrationBuilder.CreateIndex(
                name: "IX_workflow_definitions_key_version",
                table: "workflow_definitions",
                columns: new[] { "key", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_workflow_definitions_key_version",
                table: "workflow_definitions");

            migrationBuilder.AddColumn<Guid>(
                name: "tenant_id",
                table: "workflow_definitions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_workflow_definitions_tenant_id_key_version",
                table: "workflow_definitions",
                columns: new[] { "tenant_id", "key", "version" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_workflow_definitions_tenants_tenant_id",
                table: "workflow_definitions",
                column: "tenant_id",
                principalTable: "tenants",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
