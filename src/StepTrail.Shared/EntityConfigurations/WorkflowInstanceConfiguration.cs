using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StepTrail.Shared.Entities;

namespace StepTrail.Shared.EntityConfigurations;

public class WorkflowInstanceConfiguration : IEntityTypeConfiguration<WorkflowInstance>
{
    public void Configure(EntityTypeBuilder<WorkflowInstance> builder)
    {
        builder.ToTable("workflow_instances");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(i => i.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(i => i.WorkflowDefinitionId).HasColumnName("workflow_definition_id").IsRequired();
        builder.Property(i => i.ExternalKey).HasColumnName("external_key").HasMaxLength(500);
        builder.Property(i => i.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(500);
        builder.Property(i => i.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(50).IsRequired();
        builder.Property(i => i.Input).HasColumnName("input").HasColumnType("jsonb");
        builder.Property(i => i.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(i => i.UpdatedAt).HasColumnName("updated_at").IsRequired();
        builder.Property(i => i.CompletedAt).HasColumnName("completed_at");

        builder.HasIndex(i => new { i.TenantId, i.Status });
        builder.HasIndex(i => new { i.TenantId, i.ExternalKey });
        builder.HasIndex(i => new { i.TenantId, i.IdempotencyKey });

        builder.HasOne(i => i.Tenant)
            .WithMany(t => t.WorkflowInstances)
            .HasForeignKey(i => i.TenantId);

        builder.HasOne(i => i.WorkflowDefinition)
            .WithMany(w => w.Instances)
            .HasForeignKey(i => i.WorkflowDefinitionId);
    }
}
