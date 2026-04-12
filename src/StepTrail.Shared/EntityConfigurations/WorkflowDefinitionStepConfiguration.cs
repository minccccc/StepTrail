using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StepTrail.Shared.Entities;

namespace StepTrail.Shared.EntityConfigurations;

public class WorkflowDefinitionStepConfiguration : IEntityTypeConfiguration<WorkflowDefinitionStep>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinitionStep> builder)
    {
        builder.ToTable("workflow_definition_steps");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(s => s.WorkflowDefinitionId).HasColumnName("workflow_definition_id").IsRequired();
        builder.Property(s => s.StepKey).HasColumnName("step_key").HasMaxLength(200).IsRequired();
        builder.Property(s => s.StepType).HasColumnName("step_type").HasMaxLength(500).IsRequired();
        builder.Property(s => s.Order).HasColumnName("order").IsRequired();
        builder.Property(s => s.MaxAttempts).HasColumnName("max_attempts").IsRequired();
        builder.Property(s => s.RetryDelaySeconds).HasColumnName("retry_delay_seconds").IsRequired();
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(s => new { s.WorkflowDefinitionId, s.StepKey }).IsUnique();
        builder.HasIndex(s => new { s.WorkflowDefinitionId, s.Order }).IsUnique();

        builder.HasOne(s => s.WorkflowDefinition)
            .WithMany(w => w.Steps)
            .HasForeignKey(s => s.WorkflowDefinitionId);
    }
}
