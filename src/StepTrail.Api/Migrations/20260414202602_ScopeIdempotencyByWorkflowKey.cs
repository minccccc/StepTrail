using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StepTrail.Api.Migrations
{
    /// <inheritdoc />
    public partial class ScopeIdempotencyByWorkflowKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_idempotency_records_tenant_id_idempotency_key",
                table: "idempotency_records");

            migrationBuilder.AddColumn<string>(
                name: "workflow_key",
                table: "idempotency_records",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE idempotency_records AS record
                SET workflow_key = instance.workflow_definition_key
                FROM workflow_instances AS instance
                WHERE instance.id = record.workflow_instance_id;
                """);

            migrationBuilder.Sql(
                """
                DELETE FROM idempotency_records
                WHERE workflow_key IS NULL OR workflow_key = '';
                """);

            migrationBuilder.AlterColumn<string>(
                name: "workflow_key",
                table: "idempotency_records",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_records_tenant_id_workflow_key_idempotency_key",
                table: "idempotency_records",
                columns: new[] { "tenant_id", "workflow_key", "idempotency_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_idempotency_records_tenant_id_workflow_key_idempotency_key",
                table: "idempotency_records");

            migrationBuilder.DropColumn(
                name: "workflow_key",
                table: "idempotency_records");

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_records_tenant_id_idempotency_key",
                table: "idempotency_records",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true);
        }
    }
}
