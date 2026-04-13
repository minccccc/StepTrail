using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StepTrail.Shared.Entities;

namespace StepTrail.Shared.EntityConfigurations;

public class RecurringWorkflowScheduleConfiguration : IEntityTypeConfiguration<RecurringWorkflowSchedule>
{
    public void Configure(EntityTypeBuilder<RecurringWorkflowSchedule> builder)
    {
        builder.ToTable("recurring_workflow_schedules");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(s => s.WorkflowDefinitionId).HasColumnName("workflow_definition_id").IsRequired();
        builder.Property(s => s.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(s => s.IntervalSeconds).HasColumnName("interval_seconds").IsRequired();
        builder.Property(s => s.IsEnabled).HasColumnName("is_enabled").IsRequired();
        builder.Property(s => s.Input).HasColumnName("input").HasColumnType("jsonb");
        builder.Property(s => s.LastRunAt).HasColumnName("last_run_at");
        builder.Property(s => s.NextRunAt).HasColumnName("next_run_at").IsRequired();
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // One schedule per workflow definition (one schedule row per definition ID).
        builder.HasIndex(s => s.WorkflowDefinitionId).IsUnique();

        // Dispatcher query: find enabled schedules due for execution.
        builder.HasIndex(s => new { s.IsEnabled, s.NextRunAt });

        builder.HasOne(s => s.WorkflowDefinition)
            .WithMany()
            .HasForeignKey(s => s.WorkflowDefinitionId);

        builder.HasOne(s => s.Tenant)
            .WithMany()
            .HasForeignKey(s => s.TenantId);
    }
}
