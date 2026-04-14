using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StepTrail.Shared.Definitions.Persistence;

namespace StepTrail.Shared.EntityConfigurations;

public class ExecutableStepDefinitionRecordConfiguration : IEntityTypeConfiguration<ExecutableStepDefinitionRecord>
{
    public void Configure(EntityTypeBuilder<ExecutableStepDefinitionRecord> builder)
    {
        builder.ToTable("executable_step_definitions");

        builder.HasKey(step => step.Id);
        builder.Property(step => step.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(step => step.WorkflowDefinitionId).HasColumnName("workflow_definition_id").IsRequired();
        builder.Property(step => step.Key).HasColumnName("key").HasMaxLength(200).IsRequired();
        builder.Property(step => step.Order).HasColumnName("order").IsRequired();
        builder.Property(step => step.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(step => step.Configuration).HasColumnName("configuration").HasColumnType("jsonb").IsRequired();
        builder.Property(step => step.RetryPolicyOverrideKey)
            .HasColumnName("retry_policy_override_key")
            .HasMaxLength(200);

        builder.HasIndex(step => new { step.WorkflowDefinitionId, step.Key }).IsUnique();
        builder.HasIndex(step => new { step.WorkflowDefinitionId, step.Order }).IsUnique();
    }
}
