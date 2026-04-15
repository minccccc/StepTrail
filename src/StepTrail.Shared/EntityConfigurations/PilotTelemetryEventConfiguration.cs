using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StepTrail.Shared.Entities;

namespace StepTrail.Shared.EntityConfigurations;

public class PilotTelemetryEventConfiguration : IEntityTypeConfiguration<PilotTelemetryEvent>
{
    public void Configure(EntityTypeBuilder<PilotTelemetryEvent> builder)
    {
        builder.ToTable("pilot_telemetry_events");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(e => e.EventName).HasColumnName("event_name").HasMaxLength(200).IsRequired();
        builder.Property(e => e.Category).HasColumnName("category").HasMaxLength(50).IsRequired();
        builder.Property(e => e.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired();
        builder.Property(e => e.WorkflowKey).HasColumnName("workflow_key").HasMaxLength(200);
        builder.Property(e => e.WorkflowDefinitionId).HasColumnName("workflow_definition_id");
        builder.Property(e => e.WorkflowInstanceId).HasColumnName("workflow_instance_id");
        builder.Property(e => e.TriggerType).HasColumnName("trigger_type").HasMaxLength(50);
        builder.Property(e => e.StepType).HasColumnName("step_type").HasMaxLength(50);
        builder.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
        builder.Property(e => e.ActorId).HasColumnName("actor_id").HasMaxLength(200);

        builder.HasIndex(e => e.EventName);
        builder.HasIndex(e => e.Category);
        builder.HasIndex(e => e.OccurredAtUtc);
    }
}
