using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StepTrail.Shared.Entities;

namespace StepTrail.Shared.EntityConfigurations;

public class AlertRecordConfiguration : IEntityTypeConfiguration<AlertRecord>
{
    public void Configure(EntityTypeBuilder<AlertRecord> builder)
    {
        builder.ToTable("alert_records");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(a => a.AlertType).HasColumnName("alert_type").HasMaxLength(100).IsRequired();
        builder.Property(a => a.WorkflowInstanceId).HasColumnName("workflow_instance_id").IsRequired();
        builder.Property(a => a.WorkflowKey).HasColumnName("workflow_key").HasMaxLength(200);
        builder.Property(a => a.StepKey).HasColumnName("step_key").HasMaxLength(200);
        builder.Property(a => a.Attempt).HasColumnName("attempt");
        builder.Property(a => a.Cause).HasColumnName("cause").HasMaxLength(2000);
        builder.Property(a => a.GeneratedAtUtc).HasColumnName("generated_at_utc").IsRequired();

        builder.HasIndex(a => a.WorkflowInstanceId);
        builder.HasIndex(a => a.GeneratedAtUtc);
    }
}
