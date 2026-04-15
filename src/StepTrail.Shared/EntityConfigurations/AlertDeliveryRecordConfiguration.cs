using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StepTrail.Shared.Entities;

namespace StepTrail.Shared.EntityConfigurations;

public class AlertDeliveryRecordConfiguration : IEntityTypeConfiguration<AlertDeliveryRecord>
{
    public void Configure(EntityTypeBuilder<AlertDeliveryRecord> builder)
    {
        builder.ToTable("alert_delivery_records");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(d => d.AlertRecordId).HasColumnName("alert_record_id").IsRequired();
        builder.Property(d => d.Channel).HasColumnName("channel").HasMaxLength(100).IsRequired();
        builder.Property(d => d.Status).HasColumnName("status").HasMaxLength(50).IsRequired();
        builder.Property(d => d.AttemptedAtUtc).HasColumnName("attempted_at_utc").IsRequired();
        builder.Property(d => d.Error).HasColumnName("error").HasMaxLength(2000);

        builder.HasOne(d => d.AlertRecord)
            .WithMany(a => a.Deliveries)
            .HasForeignKey(d => d.AlertRecordId);
    }
}
