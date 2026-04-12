using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StepTrail.Shared.Entities;

namespace StepTrail.Shared.EntityConfigurations;

public class WorkflowDefinitionConfiguration : IEntityTypeConfiguration<WorkflowDefinition>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinition> builder)
    {
        builder.ToTable("workflow_definitions");

        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).HasColumnName("id").ValueGeneratedOnAdd();
        builder.Property(w => w.Key).HasColumnName("key").HasMaxLength(200).IsRequired();
        builder.Property(w => w.Version).HasColumnName("version").IsRequired();
        builder.Property(w => w.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(w => w.Description).HasColumnName("description").HasMaxLength(1000);
        builder.Property(w => w.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(w => new { w.Key, w.Version }).IsUnique();
    }
}
