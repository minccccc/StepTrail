using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StepTrail.Shared.Entities;

namespace StepTrail.Shared.EntityConfigurations;

public class WorkflowEventConfiguration : IEntityTypeConfiguration<WorkflowEvent>
{
    public void Configure(EntityTypeBuilder<WorkflowEvent> builder)
    {
        builder.ToTable("workflow_events");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(e => e.WorkflowInstanceId).HasColumnName("workflow_instance_id").IsRequired();
        builder.Property(e => e.StepExecutionId).HasColumnName("step_execution_id");
        builder.Property(e => e.EventType).HasColumnName("event_type").HasMaxLength(100).IsRequired();
        builder.Property(e => e.Payload).HasColumnName("payload").HasColumnType("jsonb");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(e => e.WorkflowInstanceId);

        builder.HasOne(e => e.WorkflowInstance)
            .WithMany(i => i.Events)
            .HasForeignKey(e => e.WorkflowInstanceId);

        builder.HasOne(e => e.StepExecution)
            .WithMany(s => s.Events)
            .HasForeignKey(e => e.StepExecutionId);
    }
}
