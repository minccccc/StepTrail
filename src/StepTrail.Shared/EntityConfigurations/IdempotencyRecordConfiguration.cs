using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StepTrail.Shared.Entities;

namespace StepTrail.Shared.EntityConfigurations;

public class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_records");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(r => r.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(r => r.WorkflowKey).HasColumnName("workflow_key").HasMaxLength(200).IsRequired();
        builder.Property(r => r.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(500).IsRequired();
        builder.Property(r => r.WorkflowInstanceId).HasColumnName("workflow_instance_id").IsRequired();
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();

        // Key lookup must be unique per tenant + workflow key so different workflows
        // can safely reuse the same external delivery identifier.
        builder.HasIndex(r => new { r.TenantId, r.WorkflowKey, r.IdempotencyKey }).IsUnique();

        builder.HasOne(r => r.Tenant)
            .WithMany(t => t.IdempotencyRecords)
            .HasForeignKey(r => r.TenantId);

        builder.HasOne(r => r.WorkflowInstance)
            .WithMany()
            .HasForeignKey(r => r.WorkflowInstanceId);
    }
}
