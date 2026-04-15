using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StepTrail.Shared.Entities;

namespace StepTrail.Shared.EntityConfigurations;

public class WorkflowStepExecutionConfiguration : IEntityTypeConfiguration<WorkflowStepExecution>
{
    public void Configure(EntityTypeBuilder<WorkflowStepExecution> builder)
    {
        builder.ToTable("workflow_step_executions");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(e => e.WorkflowInstanceId).HasColumnName("workflow_instance_id").IsRequired();
        builder.Property(e => e.WorkflowDefinitionStepId).HasColumnName("workflow_definition_step_id");
        builder.Property(e => e.ExecutableStepDefinitionId).HasColumnName("executable_step_definition_id");
        builder.Property(e => e.StepKey).HasColumnName("step_key").HasMaxLength(200).IsRequired();
        builder.Property(e => e.StepOrder).HasColumnName("step_order");
        builder.Property(e => e.StepType).HasColumnName("step_type").HasMaxLength(50);
        builder.Property(e => e.StepConfiguration).HasColumnName("step_configuration").HasColumnType("jsonb");
        builder.Property(e => e.RetryPolicyOverrideKey).HasColumnName("retry_policy_override_key").HasMaxLength(200);
        builder.Property(e => e.RetryPolicyJson).HasColumnName("retry_policy_json").HasColumnType("jsonb");
        builder.Property(e => e.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(e => e.Attempt).HasColumnName("attempt").IsRequired();
        builder.Property(e => e.Input).HasColumnName("input").HasColumnType("jsonb");
        builder.Property(e => e.Output).HasColumnName("output").HasColumnType("jsonb");
        builder.Property(e => e.Error).HasColumnName("error");
        builder.Property(e => e.FailureClassification).HasColumnName("failure_classification").HasMaxLength(50);
        builder.Property(e => e.ScheduledAt).HasColumnName("scheduled_at").IsRequired();
        builder.Property(e => e.LockedAt).HasColumnName("locked_at");
        builder.Property(e => e.LockedBy).HasColumnName("locked_by").HasMaxLength(200);
        builder.Property(e => e.LockExpiresAt).HasColumnName("lock_expires_at");
        builder.Property(e => e.StartedAt).HasColumnName("started_at");
        builder.Property(e => e.CompletedAt).HasColumnName("completed_at");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").IsRequired();

        // Primary polling index: workers query Pending rows scheduled to run now
        builder.HasIndex(e => new { e.Status, e.ScheduledAt });
        // Stuck-execution detection: find Running rows whose lock window expired
        builder.HasIndex(e => new { e.Status, e.LockExpiresAt });
        builder.HasIndex(e => e.WorkflowInstanceId);
        builder.HasIndex(e => new { e.WorkflowInstanceId, e.StepOrder, e.Status });

        builder.HasOne(e => e.WorkflowInstance)
            .WithMany(i => i.StepExecutions)
            .HasForeignKey(e => e.WorkflowInstanceId);

        builder.HasOne(e => e.WorkflowDefinitionStep)
            .WithMany(s => s.Executions)
            .HasForeignKey(e => e.WorkflowDefinitionStepId);
    }
}
