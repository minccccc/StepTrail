using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StepTrail.Shared.Definitions.Persistence;

namespace StepTrail.Shared.EntityConfigurations;

public class ExecutableTriggerDefinitionRecordConfiguration : IEntityTypeConfiguration<ExecutableTriggerDefinitionRecord>
{
    public void Configure(EntityTypeBuilder<ExecutableTriggerDefinitionRecord> builder)
    {
        builder.ToTable("executable_trigger_definitions");

        builder.HasKey(trigger => trigger.Id);
        builder.Property(trigger => trigger.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(trigger => trigger.WorkflowDefinitionId).HasColumnName("workflow_definition_id").IsRequired();
        builder.Property(trigger => trigger.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(trigger => trigger.Configuration).HasColumnName("configuration").HasColumnType("jsonb").IsRequired();

        builder.HasIndex(trigger => trigger.WorkflowDefinitionId).IsUnique();
    }
}
