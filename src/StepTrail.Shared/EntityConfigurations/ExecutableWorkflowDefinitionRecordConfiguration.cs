using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StepTrail.Shared.Definitions.Persistence;

namespace StepTrail.Shared.EntityConfigurations;

public class ExecutableWorkflowDefinitionRecordConfiguration : IEntityTypeConfiguration<ExecutableWorkflowDefinitionRecord>
{
    public void Configure(EntityTypeBuilder<ExecutableWorkflowDefinitionRecord> builder)
    {
        builder.ToTable("executable_workflow_definitions");

        builder.HasKey(definition => definition.Id);
        builder.Property(definition => definition.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(definition => definition.Key).HasColumnName("key").HasMaxLength(200).IsRequired();
        builder.Property(definition => definition.WebhookRouteKey).HasColumnName("webhook_route_key").HasMaxLength(200);
        builder.Property(definition => definition.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(definition => definition.Version).HasColumnName("version").IsRequired();
        builder.Property(definition => definition.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(definition => definition.Description).HasColumnName("description").HasMaxLength(1000);
        builder.Property(definition => definition.SourceTemplateKey).HasColumnName("source_template_key").HasMaxLength(200);
        builder.Property(definition => definition.SourceTemplateVersion).HasColumnName("source_template_version");
        builder.Property(definition => definition.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
        builder.Property(definition => definition.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();

        builder.HasIndex(definition => new { definition.Key, definition.Version }).IsUnique();
        builder.HasIndex(definition => new { definition.Key, definition.Status });
        builder.HasIndex(definition => definition.Key)
            .HasDatabaseName("ux_executable_workflow_definitions_active_key")
            .IsUnique()
            .HasFilter("\"status\" = 'Active'");
        builder.HasIndex(definition => definition.WebhookRouteKey)
            .HasDatabaseName("ux_executable_workflow_definitions_active_webhook_route_key")
            .IsUnique()
            .HasFilter("\"status\" = 'Active' AND \"webhook_route_key\" IS NOT NULL");

        builder.HasOne(definition => definition.TriggerDefinition)
            .WithOne(trigger => trigger.WorkflowDefinition)
            .HasForeignKey<ExecutableTriggerDefinitionRecord>(trigger => trigger.WorkflowDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(definition => definition.StepDefinitions)
            .WithOne(step => step.WorkflowDefinition)
            .HasForeignKey(step => step.WorkflowDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
